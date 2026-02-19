using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LMSupply.Transcriber.Decoding;

/// <summary>
/// Whisper autoregressive decoder using greedy search.
/// Supports both standard and merged (with KV cache) decoder models.
/// </summary>
internal sealed class WhisperDecoder
{
    private readonly InferenceSession _decoderSession;
    private readonly WhisperTokenizer _tokenizer;
    private readonly int _maxLength;

    // Input/output names for onnx-community models
    private const string InputTokensName = "input_ids";
    private const string InputEncoderHiddenStates = "encoder_hidden_states";
    private const string OutputLogitsName = "logits";
    private const string UseCacheBranchName = "use_cache_branch";

    // Alternative names used by some ONNX exports
    private static readonly string[] TokenInputNames = ["input_ids", "tokens", "decoder_input_ids"];
    private static readonly string[] EncoderInputNames = ["encoder_hidden_states", "audio", "encoder_outputs"];

    // Cached constant arrays for tensor creation (CA1861)
    private static readonly bool[] s_falseArray = [false];
    private static readonly int[] s_oneDimension = [1];

    private readonly string _actualTokenInputName;
    private readonly string _actualEncoderInputName;
    private readonly string _actualLogitsOutputName;

    // Merged model support
    private readonly bool _isMergedModel;
    private readonly List<(string Name, int[] Dims)> _pastKeyValueInputs = [];
    private readonly int _numAttentionHeads;
    private readonly int _headDim;

    public WhisperDecoder(
        InferenceSession decoderSession,
        WhisperTokenizer tokenizer,
        int maxLength = 448)
    {
        _decoderSession = decoderSession;
        _tokenizer = tokenizer;
        _maxLength = maxLength;

        // Detect input/output names from session metadata
        var inputNames = _decoderSession.InputMetadata.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outputNames = _decoderSession.OutputMetadata.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        _actualTokenInputName = TokenInputNames.FirstOrDefault(n => inputNames.Contains(n))
            ?? throw new InvalidOperationException(
                $"Could not find token input. Available inputs: {string.Join(", ", inputNames)}");

        _actualEncoderInputName = EncoderInputNames.FirstOrDefault(n => inputNames.Contains(n))
            ?? throw new InvalidOperationException(
                $"Could not find encoder input. Available inputs: {string.Join(", ", inputNames)}");

        _actualLogitsOutputName = outputNames.Contains(OutputLogitsName) ? OutputLogitsName
            : outputNames.FirstOrDefault(n => n.Contains("logit", StringComparison.OrdinalIgnoreCase))
            ?? outputNames.First();

        // Detect if this is a merged model (has use_cache_branch input)
        _isMergedModel = inputNames.Contains(UseCacheBranchName);

        if (_isMergedModel)
        {
            // Collect past_key_values input metadata
            foreach (var input in _decoderSession.InputMetadata)
            {
                if (input.Key.StartsWith("past_key_values", StringComparison.OrdinalIgnoreCase))
                {
                    var dims = input.Value.Dimensions;
                    _pastKeyValueInputs.Add((input.Key, dims));

                    // Extract attention head info from first past_key_values tensor
                    // Shape is typically [batch, num_heads, seq_len, head_dim]
                    if (_numAttentionHeads == 0 && dims.Length >= 4)
                    {
                        _numAttentionHeads = dims[1] > 0 ? dims[1] : 8; // Default to 8 if dynamic
                        _headDim = dims[3] > 0 ? dims[3] : 64; // Default to 64 if dynamic
                    }
                }
            }

            // Sort by name for consistent ordering
            _pastKeyValueInputs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Decodes encoder output to text using greedy search.
    /// </summary>
    public async Task<DecodingResult> DecodeAsync(
        float[] encoderOutput,
        int encoderSequenceLength,
        int hiddenSize,
        TranscribeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Initialize tokens with SOT sequence
            var useTimestamps = options?.WordTimestamps ?? false;
            var initialTokens = WhisperTokenizer.GetSotSequence(options?.Language, useTimestamps);
            var tokens = new List<int>(initialTokens);

            var segments = new List<TranscriptionSegment>();
            var currentSegmentTokens = new List<int>();
            var currentSegmentStart = 0.0;

            // Create encoder output tensor [1, seq_len, hidden_size]
            var encoderTensor = new DenseTensor<float>(
                encoderOutput,
                [1, encoderSequenceLength, hiddenSize]);

            string? detectedLanguage = null;
            float? languageProbability = null;
            int segmentId = 0;

            // For merged models, we'll track KV cache state
            Dictionary<string, DenseTensor<float>>? kvCache = null;

            if (_isMergedModel)
            {
                // Initialize empty KV cache tensors
                kvCache = CreateEmptyKvCache();
            }

            // Autoregressive generation loop
            while (tokens.Count < _maxLength)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Create input tensor
                var tokenArray = tokens.ToArray();
                var tokenTensor = new DenseTensor<long>(
                    tokenArray.Select(t => (long)t).ToArray(),
                    [1, tokens.Count]);

                // Build inputs list
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(_actualTokenInputName, tokenTensor),
                    NamedOnnxValue.CreateFromTensor(_actualEncoderInputName, encoderTensor)
                };

                // Add merged model specific inputs
                if (_isMergedModel && kvCache != null)
                {
                    // Add use_cache_branch = false (we're not using cache efficiently yet)
                    var useCacheTensor = new DenseTensor<bool>(s_falseArray, s_oneDimension);
                    inputs.Add(NamedOnnxValue.CreateFromTensor(UseCacheBranchName, useCacheTensor));

                    // Add past_key_values tensors
                    foreach (var (name, _) in _pastKeyValueInputs)
                    {
                        if (kvCache.TryGetValue(name, out var tensor))
                        {
                            inputs.Add(NamedOnnxValue.CreateFromTensor(name, tensor));
                        }
                    }
                }

                using var results = _decoderSession.Run(inputs);
                var logits = results.First(r => r.Name == _actualLogitsOutputName).AsTensor<float>();

                // Get logits for last position - use actual model vocab size, not tokenizer
                int vocabSize;
                float[] lastLogits;

                // Handle different logits shapes:
                // - [batch, seq_len, vocab_size] - standard transformer output
                // - [batch, vocab_size] - optimized decoder that only outputs last position
                if (logits.Dimensions.Length == 3)
                {
                    vocabSize = (int)logits.Dimensions[2];
                    lastLogits = new float[vocabSize];
                    var lastPosition = (int)logits.Dimensions[1] - 1;
                    for (int i = 0; i < vocabSize; i++)
                    {
                        lastLogits[i] = logits[0, lastPosition, i];
                    }
                }
                else if (logits.Dimensions.Length == 2)
                {
                    vocabSize = (int)logits.Dimensions[1];
                    lastLogits = new float[vocabSize];
                    for (int i = 0; i < vocabSize; i++)
                    {
                        lastLogits[i] = logits[0, i];
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Unexpected logits shape: [{string.Join(", ", logits.Dimensions.ToArray())}]");
                }

                // Apply repetition penalty to discourage repeating tokens
                const float repetitionPenalty = 1.2f;
                var recentTokens = tokens.Skip(Math.Max(0, tokens.Count - 10)).ToHashSet();
                for (int i = 0; i < lastLogits.Length; i++)
                {
                    if (recentTokens.Contains(i))
                    {
                        // Penalize recently used tokens
                        if (lastLogits[i] > 0)
                            lastLogits[i] /= repetitionPenalty;
                        else
                            lastLogits[i] *= repetitionPenalty;
                    }
                }

                // Apply temperature if specified
                if (options is { Temperature: > 0 and < 1 })
                {
                    for (int i = 0; i < lastLogits.Length; i++)
                    {
                        lastLogits[i] /= options.Temperature;
                    }
                }

                // Suppress specific tokens that cause hallucination loops
                if (tokens.Count > initialTokens.Length + 3)
                {
                    var last3 = tokens.Skip(tokens.Count - 3).ToArray();
                    if (last3[0] == last3[1] && last3[1] == last3[2])
                    {
                        // Three consecutive same tokens - heavily suppress this token
                        var repeatedToken = last3[0];
                        if (repeatedToken < lastLogits.Length)
                        {
                            lastLogits[repeatedToken] = float.NegativeInfinity;
                        }
                    }
                }

                // Greedy selection: argmax
                var nextToken = ArgMax(lastLogits);

                // Detect language from first generated token after SOT
                if (tokens.Count == initialTokens.Length && WhisperTokenizer.IsLanguageToken(nextToken))
                {
                    detectedLanguage = WhisperTokenizer.GetLanguageFromToken(nextToken);
                    languageProbability = ComputeLanguageTokenProbability(lastLogits, nextToken);
                }

                // Check for end of text
                if (nextToken == WhisperTokenizer.EndOfTextToken)
                {
                    break;
                }

                // Handle timestamp tokens
                if (WhisperTokenizer.IsTimestampToken(nextToken))
                {
                    var timestamp = WhisperTokenizer.TimestampTokenToSeconds(nextToken);

                    // Start timestamp
                    if (currentSegmentTokens.Count == 0)
                    {
                        currentSegmentStart = timestamp;
                    }
                    else
                    {
                        // End timestamp - create segment
                        var segmentText = _tokenizer.Decode(
                            currentSegmentTokens.ToArray().AsSpan(),
                            skipSpecialTokens: true);

                        if (!string.IsNullOrWhiteSpace(segmentText))
                        {
                            segments.Add(new TranscriptionSegment
                            {
                                Id = segmentId++,
                                Start = currentSegmentStart,
                                End = timestamp,
                                Text = segmentText.Trim()
                            });
                        }

                        currentSegmentTokens.Clear();
                    }
                }
                else if (!WhisperTokenizer.IsSpecialToken(nextToken))
                {
                    // Regular text token
                    currentSegmentTokens.Add(nextToken);
                }

                tokens.Add(nextToken);
            }

            // Handle remaining tokens as final segment
            if (currentSegmentTokens.Count > 0)
            {
                var segmentText = _tokenizer.Decode(
                    currentSegmentTokens.ToArray().AsSpan(),
                    skipSpecialTokens: true);

                if (!string.IsNullOrWhiteSpace(segmentText))
                {
                    segments.Add(new TranscriptionSegment
                    {
                        Id = segmentId,
                        Start = currentSegmentStart,
                        End = 30.0, // Default chunk length
                        Text = segmentText.Trim()
                    });
                }
            }

            // If no segments created (no timestamps mode), create single segment
            if (segments.Count == 0)
            {
                var allTokens = tokens.Skip(initialTokens.Length).ToArray();
                var fullText = _tokenizer.Decode(allTokens.AsSpan(), skipSpecialTokens: true);

                if (!string.IsNullOrWhiteSpace(fullText))
                {
                    segments.Add(new TranscriptionSegment
                    {
                        Id = 0,
                        Start = 0,
                        End = 30.0,
                        Text = fullText.Trim()
                    });
                }
            }

            // Combine all segment texts for full transcription
            var fullTranscription = string.Join(" ", segments.Select(s => s.Text));

            return new DecodingResult
            {
                Text = fullTranscription,
                Language = detectedLanguage ?? options?.Language ?? "en",
                LanguageProbability = languageProbability,
                Segments = segments,
                TokenCount = tokens.Count - initialTokens.Length
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Creates empty KV cache tensors for merged model initialization.
    /// </summary>
    private Dictionary<string, DenseTensor<float>> CreateEmptyKvCache()
    {
        var cache = new Dictionary<string, DenseTensor<float>>();

        foreach (var (name, dims) in _pastKeyValueInputs)
        {
            // Create zero-sized tensor for initial state
            // Shape: [batch=1, num_heads, seq_len=0, head_dim]
            var numHeads = dims[1] > 0 ? dims[1] : _numAttentionHeads;
            var headDim = dims[3] > 0 ? dims[3] : _headDim;

            var tensor = new DenseTensor<float>([1, numHeads, 0, headDim]);
            cache[name] = tensor;
        }

        return cache;
    }

    /// <summary>
    /// Computes the softmax probability of the selected language token
    /// over all language tokens in the logits.
    /// </summary>
    internal static float ComputeLanguageTokenProbability(float[] logits, int selectedToken)
    {
        var start = WhisperTokenizer.LanguageTokenStart;
        var end = Math.Min(WhisperTokenizer.LanguageTokenEnd, logits.Length - 1);

        if (start > end || selectedToken < start || selectedToken > end)
            return 0f;

        // Numerically stable softmax: subtract max before exp
        var maxLogit = float.NegativeInfinity;
        for (int i = start; i <= end; i++)
        {
            if (logits[i] > maxLogit) maxLogit = logits[i];
        }

        float sumExp = 0f;
        for (int i = start; i <= end; i++)
        {
            sumExp += MathF.Exp(logits[i] - maxLogit);
        }

        return MathF.Exp(logits[selectedToken] - maxLogit) / sumExp;
    }

    private static int ArgMax(float[] values)
    {
        int maxIndex = 0;
        float maxValue = values[0];

        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > maxValue)
            {
                maxValue = values[i];
                maxIndex = i;
            }
        }

        return maxIndex;
    }
}

/// <summary>
/// Result from the Whisper decoder.
/// </summary>
internal sealed class DecodingResult
{
    public required string Text { get; init; }
    public required string Language { get; init; }
    public float? LanguageProbability { get; init; }
    public required List<TranscriptionSegment> Segments { get; init; }
    public int TokenCount { get; init; }
}
