using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using LMSupply.Download;
using LMSupply.Exceptions;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.ChatFormatters;
using LMSupply.Hardware;
using LMSupply.Generator.Models;
using LMSupply.Llama.Server;

namespace LMSupply.Generator.Internal.Llama;

/// <summary>
/// GGUF model implementation using llama-server (standalone llama.cpp HTTP server).
/// Uses LlamaServerPool for server instance reuse across model loads.
/// </summary>
internal sealed class LlamaServerGeneratorModel : IGeneratorModel, IDiagnosticsSink
{
    private readonly ServerLease _serverLease;
    private readonly IChatFormatter _chatFormatter;
    private readonly GeneratorOptions _options;
    private readonly string _modelPath;
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly GgufMetadata? _ggufMetadata;
    private readonly string _serverVersion;
    private SelectionDiagnostics? _diagnostics;

    public void SetDiagnostics(SelectionDiagnostics diagnostics) => _diagnostics = diagnostics;
    private bool _disposed;

    private LlamaServerGeneratorModel(
        string modelId,
        string modelPath,
        ServerLease serverLease,
        IChatFormatter chatFormatter,
        GeneratorOptions options,
        int maxContextLength,
        GgufMetadata? ggufMetadata,
        string serverVersion)
    {
        ModelId = modelId;
        _modelPath = modelPath;
        _serverLease = serverLease;
        _chatFormatter = chatFormatter;
        _options = options;
        MaxContextLength = maxContextLength;
        _ggufMetadata = ggufMetadata;
        _serverVersion = serverVersion;

        // Initialize concurrency limiter
        _concurrencyLimiter = new SemaphoreSlim(
            Math.Max(1, options.MaxConcurrentRequests),
            Math.Max(1, options.MaxConcurrentRequests));
    }

    /// <summary>
    /// Loads a GGUF model using llama-server.
    /// </summary>
    public static async Task<LlamaServerGeneratorModel> LoadAsync(
        string modelId,
        string modelPath,
        IChatFormatter chatFormatter,
        GeneratorOptions options,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Get llama-server via update service (handles caching, updates, rollback)
        progress?.Report(new DownloadProgress
        {
            FileName = "llama-server",
            BytesDownloaded = 0,
            TotalBytes = 0,
            Phase = DownloadPhase.Downloading
        });

        var preferredBackend = MapProviderToBackend(options.Provider);
        var updateService = LlamaServerUpdateService.Instance;
        var updateResult = await updateService.GetServerPathAsync(
            preferredBackend,
            progress,
            cancellationToken);

        if (!updateResult.Success)
        {
            throw new InvalidOperationException(
                $"Failed to get llama-server: {updateResult.Error}");
        }

        var serverPath = updateResult.ServerPath;
        var backend = updateResult.Backend;
        var serverVersion = updateResult.NewVersion ?? updateResult.PreviousVersion;

        // 1b. Validate server version meets model requirements.
        // If the cached version is too old, trigger an immediate update and retry once
        // before throwing — avoids requiring manual cache deletion.
        if (!LlamaServerVersionRequirements.MeetsMinimum(serverVersion, chatFormatter.FormatName))
        {
            var retryResult = await updateService.CheckAndApplyUpdateAsync(
                preferredBackend, progress, cancellationToken);
            serverPath    = retryResult.ServerPath;
            backend       = retryResult.Backend;
            serverVersion = retryResult.NewVersion ?? retryResult.PreviousVersion;
        }

        LlamaServerVersionRequirements.Validate(serverVersion, chatFormatter.FormatName);

        // 2. Read GGUF metadata (best effort)
        GgufMetadata? ggufMetadata = null;
        try
        {
            ggufMetadata = await GgufMetadataReader.ReadAsync(modelPath, false, cancellationToken);
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"[LlamaServerGeneratorModel] GGUF metadata reading failed: {ex.Message}");
        }

        // 3. Configure and start llama-server
        progress?.Report(new DownloadProgress
        {
            FileName = Path.GetFileName(modelPath),
            BytesDownloaded = 30,
            TotalBytes = 100,
            Phase = DownloadPhase.Extracting
        });

        var llamaOpts = options.LlamaOptions ?? GetVramAwareLlamaOptions(modelPath, ggufMetadata);
        var contextLength = options.MaxContextLength ?? 4096;

        // Auto-calculate GPU layer count based on actual VRAM budget when using default (-1 = all)
        if (llamaOpts.GpuLayerCount == -1 && backend != LlamaServerBackend.Cpu)
        {
            var fileSize = new FileInfo(modelPath).Length;
            var profile = Hardware.HardwareProfile.Current;
            var estimate = MemoryEstimator.EstimateForGguf(
                fileSize,
                contextLength,
                availableVramBytes: profile.GpuInfo.EffectiveAvailableBytes,
                availableRamBytes: profile.SystemMemoryBytes);

            if (!estimate.CanFitInVram && estimate.RecommendedGpuLayers < estimate.TotalLayers)
            {
                llamaOpts = new LlamaOptions
                {
                    GpuLayerCount = estimate.RecommendedGpuLayers,
                    BatchSize = llamaOpts.BatchSize,
                    UBatchSize = llamaOpts.UBatchSize,
                    FlashAttention = llamaOpts.FlashAttention,
                    UseMemoryMap = llamaOpts.UseMemoryMap,
                    UseMemoryLock = llamaOpts.UseMemoryLock,
                    TypeK = llamaOpts.TypeK,
                    TypeV = llamaOpts.TypeV,
                    MainGpu = llamaOpts.MainGpu,
                    Threads = llamaOpts.Threads,
                    RopeFrequencyBase = llamaOpts.RopeFrequencyBase,
                    RopeFrequencyScale = llamaOpts.RopeFrequencyScale,
                    MultimodalProjector = llamaOpts.MultimodalProjector,
                    LoraPath = llamaOpts.LoraPath,
                    LoraScale = llamaOpts.LoraScale,
                    SpeculativeDecoding  = llamaOpts.SpeculativeDecoding,
                    DraftModelPath       = llamaOpts.DraftModelPath,
                    RopeScaling          = llamaOpts.RopeScaling,
                    YarnOriginalContext  = llamaOpts.YarnOriginalContext,
                    YarnExtensionFactor  = llamaOpts.YarnExtensionFactor,
                    YarnAttentionFactor  = llamaOpts.YarnAttentionFactor,
                    YarnBetaFast         = llamaOpts.YarnBetaFast,
                    YarnBetaSlow         = llamaOpts.YarnBetaSlow,
                };
                Trace.TraceInformation(
                    $"[LlamaServerGeneratorModel] Auto partial offload: " +
                    $"{estimate.RecommendedGpuLayers}/{estimate.TotalLayers} layers on GPU " +
                    $"(VRAM: {estimate.EstimatedVramBytes / (1024.0 * 1024 * 1024):F1}GB, " +
                    $"RAM: {estimate.EstimatedRamBytes / (1024.0 * 1024 * 1024):F1}GB)");
            }
        }

        // Auto-cap context length based on remaining VRAM after model load
        if (backend != LlamaServerBackend.Cpu)
        {
            var safeContext = EstimateSafeContextLength(modelPath, contextLength, llamaOpts.GpuLayerCount ?? -1);
            if (safeContext < contextLength)
            {
                const double mb = 1024.0 * 1024.0;
                var gpu = Hardware.HardwareProfile.Current.GpuInfo;
                var availableMb = (gpu.EffectiveAvailableBytes ?? 0) / mb;
                var totalMb = (gpu.TotalMemoryBytes ?? 0) / mb;
                var freeMb = (gpu.FreeMemoryBytes ?? gpu.TotalMemoryBytes ?? 0) / mb;
                Trace.TraceInformation(
                    $"[LlamaServerGeneratorModel] Context capped: {contextLength} → {safeContext} " +
                    $"(VRAM available={availableMb:F0}MB, free={freeMb:F0}MB, total={totalMb:F0}MB)");
                contextLength = safeContext;
            }
        }

        // Build additional arguments
        var additionalArgs = new List<string>();
        if (llamaOpts.Threads.HasValue)
        {
            additionalArgs.Add("--threads");
            additionalArgs.Add(llamaOpts.Threads.Value.ToString(CultureInfo.InvariantCulture));
        }

        // Validate speculative decoding configuration
        if (llamaOpts.SpeculativeDecoding == SpeculativeDecodingMode.DraftModel
            && string.IsNullOrEmpty(llamaOpts.DraftModelPath))
        {
            throw new InvalidOperationException(
                "SpeculativeDecoding = DraftModel requires DraftModelPath to be set.");
        }

        // Validate YaRN configuration
        if (llamaOpts.RopeScaling == RopeScalingMode.YaRN && !llamaOpts.YarnOriginalContext.HasValue)
        {
            throw new InvalidOperationException(
                "RopeScaling = YaRN requires YarnOriginalContext to be set (original training context size, e.g., 4096).");
        }

        var serverConfig = new LlamaServerConfig
        {
            ModelPath = modelPath,
            Port = 0, // Auto-assign
            ContextSize = contextLength,
            GpuLayers = llamaOpts.GpuLayerCount ?? (backend == LlamaServerBackend.Cpu ? 0 : -1),
            BatchSize = (int)(llamaOpts.BatchSize ?? 512),
            UBatchSize = llamaOpts.UBatchSize.HasValue ? (int)llamaOpts.UBatchSize.Value : null,
            Parallel = Math.Max(1, options.MaxConcurrentRequests),
            FlashAttention = llamaOpts.FlashAttention ?? false,
            // Phase 1: KV cache quantization
            CacheTypeK = ResolveKvCacheType(llamaOpts.TypeK, backend, serverVersion),
            CacheTypeV = ResolveKvCacheType(llamaOpts.TypeV, backend, serverVersion),
            // Phase 1: Memory options
            UseMemoryMap = llamaOpts.UseMemoryMap,
            UseMemoryLock = llamaOpts.UseMemoryLock,
            // Phase 1: GPU options
            MainGpu = llamaOpts.MainGpu,
            // Phase 1: RoPE options
            RopeFreqBase = llamaOpts.RopeFrequencyBase,
            RopeFreqScale = llamaOpts.RopeFrequencyScale,
            // Speculative decoding
            SpecType = ResolveSpecType(llamaOpts.SpeculativeDecoding, serverVersion),
            ModelDraft = llamaOpts.SpeculativeDecoding == SpeculativeDecodingMode.DraftModel
                ? llamaOpts.DraftModelPath : null,
            // YaRN RoPE scaling
            RopeScaling         = MapRopeScaling(llamaOpts.RopeScaling),
            YarnOriginalContext  = llamaOpts.YarnOriginalContext,
            YarnExtensionFactor  = llamaOpts.YarnExtensionFactor,
            YarnAttentionFactor  = llamaOpts.YarnAttentionFactor,
            YarnBetaFast         = llamaOpts.YarnBetaFast,
            YarnBetaSlow         = llamaOpts.YarnBetaSlow,
            // Phase 3: Multimodal support
            MultimodalProjector = llamaOpts.MultimodalProjector,
            // Phase 3: LoRA support
            LoraPath = llamaOpts.LoraPath,
            LoraScale = llamaOpts.LoraScale,
            StartupTimeout = TimeSpan.FromSeconds(120),
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            AdditionalArgs = additionalArgs.Count > 0 ? additionalArgs : null
        };

        // 4. Lease server from pool with OOM retry (reduces GPU layers on failure)
        ServerLease serverLease;
        var currentGpuLayers = serverConfig.GpuLayers;

        while (true)
        {
            try
            {
                serverLease = await LlamaServerPool.Instance.LeaseAsync(
                    serverPath,
                    serverConfig,
                    backend,
                    progress,
                    cancellationToken);
                break; // Success
            }
            catch (Exception ex) when (IsOomError(ex) && currentGpuLayers > 0)
            {
                // Reduce GPU layers by ~25% (minimum 1 layer reduction)
                var reduction = Math.Max(1, currentGpuLayers / 4);
                currentGpuLayers -= reduction;

                Trace.TraceInformation(
                    $"[LlamaServerGeneratorModel] OOM detected, retrying with {currentGpuLayers} GPU layers " +
                    $"(was {serverConfig.GpuLayers}): {ex.Message}");

                serverConfig = CloneConfigWithGpuLayers(serverConfig, currentGpuLayers);
            }
        }

        progress?.Report(new DownloadProgress
        {
            FileName = Path.GetFileName(modelPath),
            BytesDownloaded = 100,
            TotalBytes = 100,
            Phase = DownloadPhase.Complete
        });

        var model = new LlamaServerGeneratorModel(
            modelId,
            modelPath,
            serverLease,
            chatFormatter,
            options,
            SelectReportedContextLength(options, ggufMetadata, contextLength),
            ggufMetadata,
            serverVersion ?? "unknown");

        // W1: Gemma 4 tool-use risk advisory (llama.cpp #21375 / #21882 not yet merged).
        // Emitted once at load time so operators see the warning before any inference request.
        if (chatFormatter is Gemma4ChatFormatter)
        {
            Trace.TraceWarning(
                "[LlamaServerGeneratorModel] Gemma 4 model loaded. " +
                "llama.cpp #21375 (chat-template/rope) and #21882 (instruction-following) " +
                "are not yet in a stable release. " +
                "Tool-use + Korean instructional prompts may produce empty responses (Q4_K_M). " +
                "Consider Qwen2.5-7B-Instruct GGUF for reliable tool-use until upstream PRs land.");
        }

        return model;
    }

    /// <inheritdoc />
    public string ModelId { get; }

    /// <inheritdoc />
    public int MaxContextLength { get; }

    /// <inheritdoc />
    public IChatFormatter ChatFormatter => _chatFormatter;

    /// <inheritdoc />
    public bool IsGpuActive => _serverLease.Backend != LlamaServerBackend.Cpu;

    /// <inheritdoc />
    public IReadOnlyList<string> ActiveProviders => IsGpuActive
        ? new[] { $"llama-server-{_serverLease.Backend}", "CPU" }
        : new[] { "llama-server-CPU" };

    /// <inheritdoc />
    public ExecutionProvider RequestedProvider => _options.Provider;

    /// <inheritdoc />
    public long? EstimatedMemoryBytes => File.Exists(_modelPath) ? new FileInfo(_modelPath).Length * 2 : null;

    /// <summary>
    /// Gets the startup log from the llama-server process for diagnostics.
    /// </summary>
    public string? ServerStartupLog => _serverLease.Server.Info?.StartupLog;

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        GenerationOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        options ??= GenerationOptions.Default;

        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            var completionOptions = new CompletionOptions
            {
                MaxTokens = options.MaxNewTokens ?? options.MaxTokens,
                Temperature = options.Temperature,
                TopP = options.TopP,
                TopK = options.TopK,
                MinP = options.MinP,
                RepeatPenalty = options.RepetitionPenalty,
                FrequencyPenalty = options.FrequencyPenalty,
                PresencePenalty = options.PresencePenalty,
                Seed = options.Seed,
                StopSequences = MergeStopSequences(options.StopSequences),
                Grammar = options.Grammar,
                JsonSchema = options.JsonSchema
            };

            // Client-side token limit as safety net (server enforces too, but may fail)
            var maxTokens = completionOptions.MaxTokens;
            var tokenCount = 0;

            // Initialize reasoning token filter if needed
            var useReasoningFilter = options.FilterReasoningTokens || options.ExtractReasoningTokens;
            var reasoningFilter = useReasoningFilter
                ? new ReasoningTokenFilter(options.ExtractReasoningTokens)
                : null;

            await foreach (var token in _serverLease.Client.GenerateAsync(prompt, completionOptions, cancellationToken))
            {
                if (maxTokens > 0 && ++tokenCount > maxTokens)
                    break;

                if (reasoningFilter != null)
                {
                    var filtered = reasoningFilter.Process(token);
                    if (!string.IsNullOrEmpty(filtered))
                    {
                        yield return filtered;
                    }
                }
                else
                {
                    yield return token;
                }
            }

            // Flush remaining content
            if (reasoningFilter != null)
            {
                var remaining = reasoningFilter.Flush();
                if (!string.IsNullOrEmpty(remaining))
                {
                    yield return remaining;
                }
            }
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateChatAsync(
        IEnumerable<ChatMessage> messages,
        GenerationOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        options ??= GenerationOptions.Default;

        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            var trimmedMessages = await TrimToFitContextAsync(messages, options, cancellationToken);
            // Convert to llama-server format
            var augmentedMessages = MaybeInjectToolPromptFragment(trimmedMessages, options.Tools, _chatFormatter, options.EnableThinking);
            augmentedMessages = MaybeInjectThinkingToken(augmentedMessages, options.EnableThinking, _chatFormatter);
            var serverMessages = ConvertMessages(augmentedMessages);
            var chatOptions = CreateChatOptions(options);

            // Client-side token limit as safety net
            var maxTokens = options.MaxNewTokens ?? options.MaxTokens;
            var tokenCount = 0;

            // Initialize reasoning token filter if needed
            var useReasoningFilter = options.FilterReasoningTokens || options.ExtractReasoningTokens;
            var reasoningFilter = useReasoningFilter
                ? new ReasoningTokenFilter(options.ExtractReasoningTokens)
                : null;

            await foreach (var token in _serverLease.Client.GenerateChatAsync(serverMessages, chatOptions, cancellationToken))
            {
                if (maxTokens > 0 && ++tokenCount > maxTokens)
                    break;

                if (reasoningFilter != null)
                {
                    var filtered = reasoningFilter.Process(token);
                    if (!string.IsNullOrEmpty(filtered))
                    {
                        yield return filtered;
                    }
                }
                else
                {
                    yield return token;
                }
            }

            // Flush remaining content
            if (reasoningFilter != null)
            {
                var remaining = reasoningFilter.Flush();
                if (!string.IsNullOrEmpty(remaining))
                {
                    yield return remaining;
                }
            }
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateCompleteAsync(
        string prompt,
        GenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        await foreach (var token in GenerateAsync(prompt, options, cancellationToken))
        {
            sb.Append(token);
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<string> GenerateChatCompleteAsync(
        IEnumerable<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        await foreach (var token in GenerateChatAsync(messages, options, cancellationToken))
        {
            sb.Append(token);
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        // Server is already warmed up during StartAsync health check
        // Optionally perform a minimal generation
        return GenerateCompleteAsync(
            "Hi",
            new GenerationOptions { MaxTokens = 5 },
            cancellationToken);
    }

    /// <inheritdoc />
    public GeneratorModelInfo GetModelInfo() => new(
        ModelId,
        _modelPath,
        MaxContextLength,
        _chatFormatter.FormatName,
        $"llama-server-{_serverLease.Backend}")
    {
        GgufMetadata = _ggufMetadata,
        BackendLog = _serverLease.Server.Info?.StartupLog,
        RuntimeVersion = _serverVersion,
        Diagnostics = _diagnostics
    };

    /// <inheritdoc />
    public async Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(text))
            return 0;

        return await _serverLease.Client.CountTokensAsync(text, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountTokensAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var prompt = _chatFormatter.FormatPrompt(messages);
        return await CountTokensAsync(prompt, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatStreamChunk> GenerateChatStreamAsync(
        IEnumerable<ChatMessage> messages,
        GenerationOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        options ??= GenerationOptions.Default;

        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            var trimmedMessages = await TrimToFitContextAsync(messages, options, cancellationToken);
            var augmentedMessages = MaybeInjectToolPromptFragment(trimmedMessages, options.Tools, _chatFormatter, options.EnableThinking);
            augmentedMessages = MaybeInjectThinkingToken(augmentedMessages, options.EnableThinking, _chatFormatter);
            var serverMessages = ConvertMessages(augmentedMessages);
            var chatOptions = CreateChatOptions(options);

            // Client-side token limit as safety net
            var maxTokens = options.MaxNewTokens ?? options.MaxTokens;
            var tokenCount = 0;

            // Initialize reasoning token filter if needed
            var useReasoningFilter = options.FilterReasoningTokens || options.ExtractReasoningTokens;
            var reasoningFilter = useReasoningFilter
                ? new ReasoningTokenFilter(options.ExtractReasoningTokens)
                : null;

            // Initialize formatter-supplied tool-call wrapper parser if any.
            // When present, the parser is the authoritative tool-call source for
            // the turn and server-emitted tool-call deltas are suppressed
            // (ecosystem ISSUE Option D-5, 2026-05-01 — Gemma 4 wrapper extraction).
            var toolStreamParser = _chatFormatter.CreateToolCallStreamParser();

            await foreach (var data in _serverLease.Client.GenerateChatStreamAsync(
                serverMessages, chatOptions, cancellationToken))
            {
                // Safety net: stop if token limit exceeded (finish_reason chunks still pass through)
                if (data.TextDelta is not null && maxTokens > 0 && ++tokenCount > maxTokens)
                {
                    yield return new ChatStreamChunk { FinishReason = "length" };
                    break;
                }

                // Convert tool call deltas from server types to Generator types.
                // Suppressed when a formatter-supplied parser owns the channel.
                IReadOnlyList<ChatToolCallDelta>? toolCallDeltas = null;
                if (toolStreamParser is null && data.ToolCallDeltas is { Count: > 0 })
                {
                    toolCallDeltas = data.ToolCallDeltas.Select(tc => new ChatToolCallDelta
                    {
                        Index = tc.Index,
                        Id = tc.Id,
                        Name = tc.Function?.Name,
                        Arguments = tc.Function?.Arguments
                    }).ToList();
                }

                // b8994+: reasoning_content arrives as ReasoningDelta (separate from content).
                // Route it to ChatStreamChunk.ReasoningDelta when extraction is requested;
                // silently discard when only filtering is requested (server already separates it).
                string? reasoningDelta = null;
                if (data.ReasoningDelta is not null && options.ExtractReasoningTokens)
                    reasoningDelta = data.ReasoningDelta;

                // Apply reasoning filter to text delta (old-server path: <think> tags in content).
                var text = data.TextDelta;
                if (text is not null && reasoningFilter is not null)
                {
                    text = reasoningFilter.Process(text);
                    if (string.IsNullOrEmpty(text))
                        text = null;
                }

                // Route remaining text through the formatter-supplied wrapper parser.
                if (text is not null && toolStreamParser is not null)
                {
                    var parsed = toolStreamParser.Feed(text);
                    text = parsed.Text;
                    if (parsed.ToolCalls is { Count: > 0 })
                    {
                        toolCallDeltas = parsed.ToolCalls;
                    }
                }

                // Yield structured chunk
                if (text is not null || reasoningDelta is not null || toolCallDeltas is not null || data.FinishReason is not null)
                {
                    yield return new ChatStreamChunk
                    {
                        Text = text,
                        ReasoningDelta = reasoningDelta,
                        ToolCalls = toolCallDeltas,
                        FinishReason = data.FinishReason
                    };
                }
            }

            // Flush remaining reasoning content
            if (reasoningFilter is not null)
            {
                var remaining = reasoningFilter.Flush();
                if (!string.IsNullOrEmpty(remaining))
                {
                    var residual = remaining;
                    IReadOnlyList<ChatToolCallDelta>? residualCalls = null;
                    if (toolStreamParser is not null)
                    {
                        var parsed = toolStreamParser.Feed(residual);
                        residual = parsed.Text;
                        residualCalls = parsed.ToolCalls;
                    }
                    if (residual is not null || residualCalls is not null)
                    {
                        yield return new ChatStreamChunk { Text = residual, ToolCalls = residualCalls };
                    }
                }
            }

            // Flush formatter-supplied parser (releases trailing text outside any wrapper;
            // incomplete wrapper bodies are discarded — see Gemma4ToolCallStreamParser).
            if (toolStreamParser is not null)
            {
                var flushed = toolStreamParser.Flush();
                if (flushed.Text is not null || flushed.ToolCalls is { Count: > 0 })
                {
                    yield return new ChatStreamChunk
                    {
                        Text = flushed.Text,
                        ToolCalls = flushed.ToolCalls
                    };
                }
            }
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>
    /// Generates a chat completion with tool calling support.
    /// Returns structured result that may contain tool calls.
    /// </summary>
    public async Task<ChatCompletionResult> GenerateChatWithToolsAsync(
        IEnumerable<ChatMessage> messages,
        GenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        options ??= GenerationOptions.Default;

        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            var trimmedMessages = await TrimToFitContextAsync(messages, options, cancellationToken);
            var augmentedMessages = MaybeInjectToolPromptFragment(trimmedMessages, options.Tools, _chatFormatter, options.EnableThinking);
            augmentedMessages = MaybeInjectThinkingToken(augmentedMessages, options.EnableThinking, _chatFormatter);
            var serverMessages = ConvertMessages(augmentedMessages);
            var chatOptions = CreateChatOptions(options);

            var response = await _serverLease.Client.GenerateChatWithToolsAsync(
                serverMessages, chatOptions, cancellationToken);

            var choice = response.Choices?.FirstOrDefault();
            var message = choice?.Message;

            return new ChatCompletionResult
            {
                Content = message?.Content,
                FinishReason = choice?.FinishReason,
                ToolCalls = message?.ToolCalls?.Select(tc => new ChatToolCall(
                    tc.Id ?? string.Empty,
                    tc.Function?.Name ?? string.Empty,
                    tc.Function?.Arguments ?? string.Empty
                )).ToList()
            };
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    private static IEnumerable<ChatCompletionMessage> ConvertMessages(IEnumerable<ChatMessage> messages)
    {
        return messages.Select(m => new ChatCompletionMessage
        {
            Role = m.Role switch
            {
                ChatRole.System => "system",
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.Tool => "tool",
                _ => "user"
            },
            Content = m.Content,
            ToolCallId = m.ToolCallId,
            ToolCalls = m.ToolCalls?.Select(tc => new ToolCallMessage
            {
                Id = tc.Id,
                Type = "function",
                Function = new FunctionCallMessage
                {
                    Name = tc.FunctionName,
                    Arguments = tc.Arguments
                }
            }).ToList()
        });
    }

    private ChatCompletionOptions CreateChatOptions(GenerationOptions options)
    {
        return new ChatCompletionOptions
        {
            MaxTokens = options.MaxNewTokens ?? options.MaxTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK,
            MinP = options.MinP,
            RepeatPenalty = options.RepetitionPenalty,
            FrequencyPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
            Seed = options.Seed,
            StopSequences = MergeStopSequences(options.StopSequences),
            Grammar = options.Grammar,
            JsonSchema = options.JsonSchema,
            Tools = options.Tools?.Select(t => new LMSupply.Llama.Server.ToolDefinition
            {
                Type = "function",
                Function = new LMSupply.Llama.Server.FunctionDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.Parameters
                }
            }).ToList()
        };
    }

    private List<string>? MergeStopSequences(IReadOnlyList<string>? userStops)
    {
        var merged = new List<string>();

        // 1. Stop sequences from chat formatter
        merged.AddRange(_chatFormatter.GetStopSequences());

        // 2. User-provided stop sequences
        if (userStops != null)
        {
            foreach (var stop in userStops)
            {
                if (!merged.Contains(stop, StringComparer.Ordinal))
                {
                    merged.Add(stop);
                }
            }
        }

        return merged.Count > 0 ? merged : null;
    }

    /// <summary>
    /// Resolves KV cache type to llama-server CLI string.
    /// Auto selects based on backend and server version.
    /// </summary>
    internal static string? ResolveKvCacheType(
        KvCacheQuantizationType type,
        LlamaServerBackend backend,
        string? serverVersion)
    {
        if (type == KvCacheQuantizationType.Auto)
            type = SelectAutoKvCache(backend, serverVersion);

        return type switch
        {
            KvCacheQuantizationType.Q8_0 => "q8_0",
            KvCacheQuantizationType.Q4_0 => "q4_0",
            KvCacheQuantizationType.F32  => "f32",
            _                            => null   // F16 = llama-server default
        };
    }

    private static KvCacheQuantizationType SelectAutoKvCache(LlamaServerBackend backend, string? serverVersion)
    {
        return backend switch
        {
            LlamaServerBackend.Cuda12 or LlamaServerBackend.Cuda13
                or LlamaServerBackend.Metal or LlamaServerBackend.Hip
                => KvCacheQuantizationType.Q8_0,
            LlamaServerBackend.Vulkan
                => IsFeatureSupported("kv-q8-vulkan", serverVersion)
                    ? KvCacheQuantizationType.Q8_0
                    : KvCacheQuantizationType.F16,
            _ => KvCacheQuantizationType.F16   // Cpu, Sycl
        };
    }

    /// <summary>
    /// Resolves speculative decoding mode to llama-server --spec-type value.
    /// </summary>
    internal static string? ResolveSpecType(
        SpeculativeDecodingMode mode,
        string? serverVersion)
    {
        // b8994+ renamed --spec-type ngram to ngram-simple.
        // Auto probes the newer name first, falls back to the old name for b8500-b8993.
        bool hasNgramSimple = IsFeatureSupported("spec-ngram-simple", serverVersion);
        bool hasNgram       = IsFeatureSupported("spec-ngram",        serverVersion);

        return mode switch
        {
            SpeculativeDecodingMode.None       => null,
            SpeculativeDecodingMode.Ngram      => hasNgramSimple ? "ngram-simple" : "ngram",
            SpeculativeDecodingMode.DraftModel => null,  // handled via ModelDraft
            SpeculativeDecodingMode.Auto       => hasNgramSimple ? "ngram-simple" :
                                                  hasNgram       ? "ngram"        : null,
            _ => null
        };
    }

    private static bool IsFeatureSupported(string featureKey, string? serverVersion)
    {
        var build = LlamaServerVersionRequirements.ParseBuildNumber(serverVersion);
        var minBuild = LlamaServerVersionRequirements.GetMinimumBuild(featureKey);
        return build.HasValue && minBuild.HasValue && build.Value >= minBuild.Value;
    }

    private static string? MapRopeScaling(RopeScalingMode mode) => mode switch
    {
        RopeScalingMode.Linear   => "linear",
        RopeScalingMode.YaRN    => "yarn",
        RopeScalingMode.LongRoPE => "longrope",
        _                        => null  // Default = passthrough
    };

    private static LlamaServerBackend MapProviderToBackend(ExecutionProvider provider)
    {
        // Explicit provider selection
        if (provider != ExecutionProvider.Auto)
        {
            return provider switch
            {
                ExecutionProvider.Cpu => LlamaServerBackend.Cpu,
                ExecutionProvider.Cuda => LlamaServerBackend.Cuda12,
                ExecutionProvider.DirectML => LlamaServerBackend.Vulkan,
                ExecutionProvider.CoreML => LlamaServerBackend.Metal,
                _ => LlamaServerBackend.Cpu
            };
        }

        // Auto: Detect optimal backend based on actual GPU
        var gpuInfo = Hardware.HardwareProfile.Current.GpuInfo;

        return gpuInfo.Vendor switch
        {
            // NVIDIA: Prefer CUDA for best performance
            Runtime.GpuVendor.Nvidia => LlamaServerBackend.Cuda12,

            // AMD: Vulkan on Windows, ROCm (Hip) on Linux
            Runtime.GpuVendor.Amd => OperatingSystem.IsLinux()
                ? LlamaServerBackend.Hip
                : LlamaServerBackend.Vulkan,

            // Intel: Vulkan for modern iGPUs (Iris, Arc), CPU for legacy (HD Graphics)
            // Note: Intel iGPUs use shared memory, so TotalMemoryBytes is not reliable
            Runtime.GpuVendor.Intel => IsModernIntelGpu(gpuInfo.DeviceName)
                ? LlamaServerBackend.Vulkan
                : LlamaServerBackend.Cpu,

            // Apple: Metal
            Runtime.GpuVendor.Apple => LlamaServerBackend.Metal,

            // Unknown but has DirectML support: use Vulkan
            _ when gpuInfo.DirectMLSupported => LlamaServerBackend.Vulkan,

            // Fallback to CPU
            _ => LlamaServerBackend.Cpu
        };
    }

    /// <summary>
    /// Checks if the Intel GPU is modern enough to use Vulkan acceleration.
    /// Modern: Iris, Arc, UHD 600+ series
    /// Legacy: HD Graphics 4000 and older
    /// </summary>
    private static bool IsModernIntelGpu(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
            return false;

        var name = deviceName.ToUpperInvariant();

        // Modern Intel GPUs that work well with Vulkan
        if (name.Contains("IRIS") || name.Contains("ARC"))
            return true;

        // UHD Graphics 600 series and newer (Gen 9.5+)
        if (name.Contains("UHD"))
            return true;

        // Intel Xe Graphics
        if (name.Contains(" XE"))
            return true;

        // Legacy HD Graphics - fall back to CPU
        // HD Graphics 4000, 5000, 6000 are too old for good Vulkan performance
        return false;
    }

    /// <summary>
    /// Estimates the maximum safe context length based on VRAM remaining after model weights.
    /// Returns the original requestedContext if it fits, otherwise a capped value.
    /// </summary>
    internal static int EstimateSafeContextLength(string modelPath, int requestedContext, int gpuLayerCount)
    {
        var profile = Hardware.HardwareProfile.Current;
        var availableVram = profile.GpuInfo.EffectiveAvailableBytes ?? 0;
        if (availableVram <= 0 || gpuLayerCount == 0)
            return requestedContext; // CPU-only, no VRAM constraint

        var modelFileSize = new FileInfo(modelPath).Length;
        var modelMemory = (long)(modelFileSize * 1.1);
        const long vramBuffer = 512L * 1024 * 1024; // 500MB safety buffer
        var remainingVram = Math.Max(0, availableVram - modelMemory - vramBuffer);

        // KV cache per token ≈ 2(K+V) × layers × hiddenSize × 2(FP16 bytes)
        var kvBytesPerToken = Core.Download.AvailableMemory.EstimateKvCacheBytes(modelFileSize, 1);
        if (kvBytesPerToken <= 0)
            return requestedContext;

        var safeContext = (int)(remainingVram / kvBytesPerToken);
        safeContext = Math.Max(512, safeContext); // minimum 512 tokens

        return Math.Min(requestedContext, safeContext);
    }

    /// <summary>
    /// Creates a copy of a LlamaServerConfig with a different GpuLayers value.
    /// </summary>
    private static LlamaServerConfig CloneConfigWithGpuLayers(LlamaServerConfig source, int gpuLayers) => new()
    {
        ModelPath = source.ModelPath,
        Port = source.Port,
        ContextSize = source.ContextSize,
        GpuLayers = gpuLayers,
        BatchSize = source.BatchSize,
        UBatchSize = source.UBatchSize,
        Parallel = source.Parallel,
        FlashAttention = source.FlashAttention,
        CacheTypeK = source.CacheTypeK,
        CacheTypeV = source.CacheTypeV,
        UseMemoryMap = source.UseMemoryMap,
        UseMemoryLock = source.UseMemoryLock,
        MainGpu = source.MainGpu,
        RopeFreqBase = source.RopeFreqBase,
        RopeFreqScale = source.RopeFreqScale,
        SpecType            = source.SpecType,
        ModelDraft          = source.ModelDraft,
        RopeScaling         = source.RopeScaling,
        YarnOriginalContext = source.YarnOriginalContext,
        YarnExtensionFactor = source.YarnExtensionFactor,
        YarnAttentionFactor = source.YarnAttentionFactor,
        YarnBetaFast        = source.YarnBetaFast,
        YarnBetaSlow        = source.YarnBetaSlow,
        MultimodalProjector = source.MultimodalProjector,
        LoraPath = source.LoraPath,
        LoraScale = source.LoraScale,
        Mode = source.Mode,
        Pooling = source.Pooling,
        StartupTimeout = source.StartupTimeout,
        ShutdownTimeout = source.ShutdownTimeout,
        AdditionalArgs = source.AdditionalArgs
    };

    /// <summary>
    /// Checks if an exception is an out-of-memory error from the GPU runtime.
    /// </summary>
    internal static bool IsOomError(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("CUDA_ERROR_OUT_OF_MEMORY", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Could not allocate")
            || msg.Contains("ggml_backend_cuda_buffer_type_alloc_buffer");
    }

    /// <summary>
    /// Gets VRAM-aware LlamaOptions using actual model size and GPU information.
    /// Uses GgufModelInfo metadata when available, falls back to file-size estimation.
    /// </summary>
    private static LlamaOptions GetVramAwareLlamaOptions(string modelPath, GgufMetadata? ggufMetadata)
    {
        var gpu = Hardware.HardwareProfile.Current.GpuInfo;

        // Determine model size: prefer GGUF metadata, fall back to file size
        long modelSizeBytes;
        if (ggufMetadata is not null)
        {
            // Use file size as the most accurate measure of on-disk model weight size
            modelSizeBytes = new FileInfo(modelPath).Length;
        }
        else
        {
            // No metadata: estimate from file size with runtime overhead factor
            var fileSize = new FileInfo(modelPath).Length;
            modelSizeBytes = (long)(fileSize * 1.1); // ~10% runtime overhead
        }

        // Determine total layers from metadata or estimate from file size
        var totalLayers = ggufMetadata?.LayerCount ?? EstimateTotalLayers(new FileInfo(modelPath).Length);

        return LlamaOptions.GetOptimalForHardware(gpu, modelSizeBytes, totalLayers);
    }

    /// <summary>
    /// Estimates total layer count from GGUF file size when metadata is not available.
    /// </summary>
    internal static int EstimateTotalLayers(long fileSizeBytes)
    {
        return fileSizeBytes switch
        {
            < 2L * 1024 * 1024 * 1024 => 22,
            < 5L * 1024 * 1024 * 1024 => 28,
            < 10L * 1024 * 1024 * 1024 => 32,
            _ => 40
        };
    }

    /// <summary>
    /// Conditionally prepends a textual reinforcement of the active tool schemas as a
    /// system message when the formatter opts in via
    /// <see cref="IChatFormatter.RenderToolPromptFragment"/>. Small/quantized models
    /// (Gemma 4 E4B at gguf:default) misinterpret llama-server's raw JSON-schema
    /// rendering and emit empty tool args; the textual fragment raises first-attempt
    /// success (ecosystem ISSUE Option D-1, 2026-04-30).
    /// </summary>
    /// <remarks>
    /// Returns the original sequence unchanged when the formatter returns <c>null</c>
    /// (default for all formatters except Gemma 4) or when no tools are passed —
    /// avoids polluting other model families' prompts with redundant text.
    /// </remarks>
    /// <summary>
    /// Prepends the formatter's thinking token to the first system message when
    /// <paramref name="enableThinking"/> is <c>true</c> and the formatter opts in via
    /// <see cref="IChatFormatter.GetThinkingToken"/>. If no system message exists, a
    /// bare system message containing only the token is prepended.
    /// </summary>
    /// <remarks>
    /// For Gemma 4 E2B/E4B, Google recommends activating thinking mode (via
    /// <c>&lt;|think|&gt;</c>) when complex function calling is required; the model reasons
    /// internally before deciding to invoke a tool.
    /// Call after <see cref="MaybeInjectToolPromptFragment"/> so the thinking token
    /// lands at the top of the combined system message.
    /// </remarks>
    internal static IEnumerable<ChatMessage> MaybeInjectThinkingToken(
        IEnumerable<ChatMessage> messages,
        bool enableThinking,
        IChatFormatter formatter)
    {
        var token = enableThinking ? formatter.GetThinkingToken() : null;
        if (token is null)
            return messages;

        var list = messages.ToList();
        var idx = list.FindIndex(m => m.Role == ChatRole.System);
        if (idx >= 0)
            list[idx] = ChatMessage.System(token + "\n" + list[idx].Content);
        else
            list.Insert(0, ChatMessage.System(token));
        return list;
    }

    internal static IEnumerable<ChatMessage> MaybeInjectToolPromptFragment(
        IEnumerable<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition>? tools,
        IChatFormatter formatter,
        bool enableThinking = false)
    {
        var fragment = enableThinking
            ? formatter.RenderToolPromptFragmentWhenThinking(tools)
            : formatter.RenderToolPromptFragment(tools);

        if (string.IsNullOrEmpty(fragment))
        {
            foreach (var msg in messages)
            {
                yield return msg;
            }
            yield break;
        }

        yield return ChatMessage.System(fragment);
        foreach (var msg in messages)
        {
            yield return msg;
        }
    }

    /// <summary>
    /// Selects the context length to report as the model's capability.
    /// Priority: explicit user cap &gt; GGUF metadata capability &gt; VRAM-capped session budget.
    /// </summary>
    internal static int SelectReportedContextLength(
        GeneratorOptions options,
        GgufMetadata? ggufMetadata,
        int vramCappedBudget)
        => options.MaxContextLength ?? ggufMetadata?.ContextLength ?? vramCappedBudget;

    /// <summary>
    /// Removes the oldest non-system conversation turn in-place.
    /// A turn is: User + optional (Assistant + optional Tool*).
    /// System messages are never removed. The last User message is protected.
    /// Tool call / result pairs are treated atomically: removing an Assistant with ToolCalls
    /// also removes the following Tool result messages.
    /// </summary>
    /// <returns><c>true</c> if a turn was removed; <c>false</c> when trimming is impossible.</returns>
    internal static bool TryTrimOldestTurn(List<ChatMessage> messages)
    {
        var firstNonSystem = messages.FindIndex(m => m.Role != ChatRole.System);
        if (firstNonSystem < 0)
            return false;

        var turnEnd = firstNonSystem;
        if (turnEnd < messages.Count && messages[turnEnd].Role == ChatRole.User)
            turnEnd++;
        if (turnEnd < messages.Count && messages[turnEnd].Role == ChatRole.Assistant)
            turnEnd++;
        while (turnEnd < messages.Count && messages[turnEnd].Role == ChatRole.Tool)
            turnEnd++;

        // Ensure at least one User message remains after removing this turn
        var hasRemainingUser = false;
        for (var i = turnEnd; i < messages.Count; i++)
        {
            if (messages[i].Role == ChatRole.User)
            {
                hasRemainingUser = true;
                break;
            }
        }
        if (!hasRemainingUser)
            return false;

        messages.RemoveRange(firstNonSystem, turnEnd - firstNonSystem);
        return true;
    }

    /// <summary>
    /// Returns a (possibly trimmed) copy of <paramref name="messages"/> whose augmented prompt
    /// fits within the input token budget: MaxContextLength minus reserved output tokens.
    /// Skips trimming when MaxContextLength is unknown (0).
    /// Throws <see cref="ContextLengthExceededException"/> if the current turn alone exceeds the budget.
    /// </summary>
    private async Task<List<ChatMessage>> TrimToFitContextAsync(
        IEnumerable<ChatMessage> messages,
        GenerationOptions options,
        CancellationToken cancellationToken)
    {
        if (MaxContextLength <= 0)
            return messages.ToList();

        var outputReserved = options.MaxNewTokens ?? options.MaxTokens;
        var inputBudget = MaxContextLength - outputReserved;
        if (inputBudget <= 0)
            return messages.ToList();

        var list = messages.ToList();

        while (true)
        {
            var augmented = MaybeInjectToolPromptFragment(list, options.Tools, _chatFormatter, options.EnableThinking);
            augmented = MaybeInjectThinkingToken(augmented, options.EnableThinking, _chatFormatter);
            var prompt = _chatFormatter.FormatPrompt(augmented);
            var tokenCount = await _serverLease.Client.CountTokensAsync(prompt, cancellationToken);

            if (tokenCount <= inputBudget)
                return list;

            if (!TryTrimOldestTurn(list))
                throw new ContextLengthExceededException(tokenCount, MaxContextLength);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _concurrencyLimiter.Dispose();

        // Return server to pool (does not terminate the server)
        await _serverLease.DisposeAsync();
    }
}
