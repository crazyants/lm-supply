using System.Diagnostics;
using LMSupply.Download;
using LMSupply.Inference;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LMSupply.Embedder.Inference;

/// <summary>
/// ONNX Runtime inference engine for embedding models.
/// </summary>
internal sealed class OnnxInferenceEngine : IDisposable
{
    private InferenceSession _session;
    private readonly bool _hasTokenTypeIds;
    private readonly string _outputName;
    private bool _isGpuProvider;
    private bool _isGpuActive;
    private IReadOnlyList<string> _activeProviders;
    private readonly ExecutionProvider _requestedProvider;
    private readonly string _modelPath;
    private readonly List<ExecutionProvider> _blacklistedProviders = new();
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    public int HiddenSize { get; }

    /// <summary>
    /// Gets whether GPU acceleration is being used for inference.
    /// </summary>
    public bool IsGpuActive => _isGpuActive;

    /// <summary>
    /// Gets the list of active execution providers.
    /// </summary>
    public IReadOnlyList<string> ActiveProviders => _activeProviders;

    /// <summary>
    /// Gets the execution provider that was requested.
    /// </summary>
    public ExecutionProvider RequestedProvider => _requestedProvider;

    private OnnxInferenceEngine(
        InferenceSession session,
        int hiddenSize,
        bool hasTokenTypeIds,
        string outputName,
        bool isGpuProvider,
        bool isGpuActive,
        IReadOnlyList<string> activeProviders,
        ExecutionProvider requestedProvider,
        string modelPath)
    {
        _session = session;
        HiddenSize = hiddenSize;
        _hasTokenTypeIds = hasTokenTypeIds;
        _outputName = outputName;
        _isGpuProvider = isGpuProvider;
        _isGpuActive = isGpuActive;
        _activeProviders = activeProviders;
        _requestedProvider = requestedProvider;
        _modelPath = modelPath;
    }

    /// <summary>
    /// Creates an inference engine from an ONNX model file asynchronously.
    /// This method ensures runtime binaries are available before creating the session.
    /// </summary>
    /// <param name="modelPath">Path to the ONNX model file.</param>
    /// <param name="provider">The execution provider to use.</param>
    /// <param name="progress">Optional progress reporter for binary downloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A configured inference engine.</returns>
    public static async Task<OnnxInferenceEngine> CreateAsync(
        string modelPath,
        ExecutionProvider provider,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(modelPath))
            throw new ModelNotFoundException("Model file not found", modelPath);

        var result = await OnnxSessionFactory.CreateWithInfoAsync(
            modelPath,
            provider,
            ConfigureOptions,
            progress,
            cancellationToken);

        return CreateFromSessionResult(result, modelPath);
    }

    /// <summary>
    /// Creates an inference engine from an ONNX model file.
    /// Note: This assumes runtime binaries are already available. For lazy loading, use CreateAsync.
    /// </summary>
    public static OnnxInferenceEngine Create(string modelPath, ExecutionProvider provider)
    {
        if (!File.Exists(modelPath))
            throw new ModelNotFoundException("Model file not found", modelPath);

        var session = OnnxSessionFactory.Create(modelPath, provider, ConfigureOptions);
        var activeProviders = OnnxSessionFactory.GetActiveProviders(session);
        var isGpuActive = activeProviders.Any(p =>
            p.Contains("CUDA", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("DML", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("CoreML", StringComparison.OrdinalIgnoreCase));

        return CreateFromSession(session, IsGpuProvider(provider), isGpuActive, activeProviders, provider, modelPath);
    }

    private static bool IsGpuProvider(ExecutionProvider provider)
    {
        return provider is ExecutionProvider.Cuda
            or ExecutionProvider.DirectML
            or ExecutionProvider.CoreML
            or ExecutionProvider.Auto; // Auto may select GPU, so treat as GPU for safety
    }

    private static void ConfigureOptions(SessionOptions options)
    {
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        options.EnableCpuMemArena = true;
        options.EnableMemoryPattern = true;
        options.IntraOpNumThreads = Environment.ProcessorCount;
        options.InterOpNumThreads = 1;
    }

    private static OnnxInferenceEngine CreateFromSessionResult(SessionCreationResult result, string modelPath)
    {
        return CreateFromSession(
            result.Session,
            IsGpuProvider(result.RequestedProvider),
            result.IsGpuActive,
            result.ActiveProviders,
            result.RequestedProvider,
            modelPath);
    }

    private static OnnxInferenceEngine CreateFromSession(
        InferenceSession session,
        bool isGpuProvider,
        bool isGpuActive,
        IReadOnlyList<string> activeProviders,
        ExecutionProvider requestedProvider,
        string modelPath)
    {
        // Detect model configuration from metadata
        var inputNames = session.InputMetadata.Keys.ToHashSet();
        bool hasTokenTypeIds = inputNames.Contains("token_type_ids");

        // Get output name and hidden size
        var outputMeta = session.OutputMetadata.First();
        string outputName = outputMeta.Key;
        int hiddenSize = (int)outputMeta.Value.Dimensions[^1]; // Last dimension is hidden size

        return new OnnxInferenceEngine(
            session,
            hiddenSize,
            hasTokenTypeIds,
            outputName,
            isGpuProvider,
            isGpuActive,
            activeProviders,
            requestedProvider,
            modelPath);
    }

    /// <summary>
    /// Runs inference for a single sequence.
    /// </summary>
    public float[] RunInference(long[] inputIds, long[] attentionMask)
    {
        return RunInferenceInternal(inputIds, attentionMask, allowRetry: true);
    }

    private float[] RunInferenceInternal(long[] inputIds, long[] attentionMask, bool allowRetry)
    {
        int seqLength = inputIds.Length;

        // Create input tensors
        var inputIdsTensor = new DenseTensor<long>(inputIds, [1, seqLength]);
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, [1, seqLength]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
        };

        // Add token_type_ids if model expects it
        if (_hasTokenTypeIds)
        {
            var tokenTypeIds = new long[seqLength]; // All zeros
            var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, [1, seqLength]);
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor));
        }

        // Serialize access to InferenceSession (not thread-safe for concurrent Run calls)
        _sessionLock.Wait();
        try
        {
            try
            {
                using var results = _session.Run(inputs);
                var output = results[0].AsTensor<float>();

                // Output shape: [1, seqLength, hiddenSize]
                // Copy to flat array
                var outputArray = new float[seqLength * HiddenSize];
                int idx = 0;
                for (int seq = 0; seq < seqLength; seq++)
                {
                    for (int dim = 0; dim < HiddenSize; dim++)
                    {
                        outputArray[idx++] = output[0, seq, dim];
                    }
                }

                return outputArray;
            }
            catch (OnnxRuntimeException ex) when (allowRetry && _requestedProvider == ExecutionProvider.Auto && TryFallback(ex))
            {
                // After successful fallback, retry exactly once on the new session.
                // Pass allowRetry: false to avoid runaway recursion if the next provider also fails
                // on the same input (the outer call will see the propagated exception).
                return RunInferenceInternal(inputIds, attentionMask, allowRetry: false);
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>
    /// Attempts to recreate the inference session on the next provider in the fallback chain
    /// after the current provider's session crashed at run time. Must be called while holding
    /// <see cref="_sessionLock"/>. Returns true on success; false if no remaining provider can run.
    /// </summary>
    private bool TryFallback(OnnxRuntimeException ex)
    {
        // Identify the failing provider from the current active set.
        var failedProvider = MapActiveToProvider(_activeProviders);
        if (failedProvider is null)
        {
            Trace.TraceWarning(
                "[OnnxInferenceEngine] Inference failed but active provider could not be identified; not attempting fallback.");
            return false;
        }

        if (_blacklistedProviders.Contains(failedProvider.Value))
        {
            // Already tried recovery for this provider — give up.
            return false;
        }

        _blacklistedProviders.Add(failedProvider.Value);

        Trace.TraceWarning(
            $"[OnnxInferenceEngine] Inference failed on {failedProvider} ({ex.GetType().Name}). " +
            $"Attempting fallback to next provider. Original message: {Truncate(ex.Message, 200)}");

        try
        {
            var result = OnnxSessionFactory.CreateWithInfoAsync(
                _modelPath,
                ExecutionProvider.Auto,
                _blacklistedProviders.ToArray(),
                ConfigureOptions).GetAwaiter().GetResult();

            // Verify a different provider was actually selected.
            var newProvider = MapActiveToProvider(result.ActiveProviders);
            if (newProvider is null || _blacklistedProviders.Contains(newProvider.Value))
            {
                Trace.TraceWarning(
                    $"[OnnxInferenceEngine] Fallback produced no usable alternative provider " +
                    $"(blacklist=[{string.Join(",", _blacklistedProviders)}]). Surfacing original exception.");
                result.Session.Dispose();
                return false;
            }

            // Swap the session under the lock.
            _session.Dispose();
            _session = result.Session;
            _activeProviders = result.ActiveProviders;
            _isGpuActive = result.IsGpuActive;
            _isGpuProvider = IsGpuProvider(newProvider.Value);

            Trace.TraceWarning(
                $"[OnnxInferenceEngine] Recovered: now running on {string.Join("+", _activeProviders)}.");
            return true;
        }
        catch (Exception fallbackEx)
        {
            Trace.TraceError(
                $"[OnnxInferenceEngine] Fallback session creation failed: {Truncate(fallbackEx.Message, 200)}");
            return false;
        }
    }

    private static ExecutionProvider? MapActiveToProvider(IReadOnlyList<string> activeProviders)
    {
        // The first non-CPU provider in the list is the primary.
        foreach (var p in activeProviders)
        {
            if (p.Contains("CUDA", StringComparison.OrdinalIgnoreCase))
                return ExecutionProvider.Cuda;
            if (p.Contains("DML", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("DirectML", StringComparison.OrdinalIgnoreCase))
                return ExecutionProvider.DirectML;
            if (p.Contains("CoreML", StringComparison.OrdinalIgnoreCase))
                return ExecutionProvider.CoreML;
        }
        if (activeProviders.Any(p => p.Contains("CPU", StringComparison.OrdinalIgnoreCase)))
            return ExecutionProvider.Cpu;
        return null;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    /// <summary>
    /// Runs batch inference for multiple sequences (sequential).
    /// </summary>
    public float[][] RunBatchInference(long[][] inputIds, long[][] attentionMasks)
    {
        int batchSize = inputIds.Length;
        var results = new float[batchSize][];

        for (int i = 0; i < batchSize; i++)
        {
            results[i] = RunInference(inputIds[i], attentionMasks[i]);
        }

        return results;
    }

    /// <summary>
    /// Runs batch inference sequentially. InferenceSession is not thread-safe for concurrent
    /// Run() calls, so all batch items are processed serially under the session lock.
    /// </summary>
    public float[][] RunBatchInferenceParallel(long[][] inputIds, long[][] attentionMasks)
    {
        int batchSize = inputIds.Length;
        var results = new float[batchSize][];

        for (int i = 0; i < batchSize; i++)
        {
            results[i] = RunInference(inputIds[i], attentionMasks[i]);
        }

        return results;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _sessionLock.Dispose();
    }
}
