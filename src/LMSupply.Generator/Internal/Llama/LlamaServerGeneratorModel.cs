using System.Globalization;
using System.Runtime.CompilerServices;
using LMSupply.Download;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;
using LMSupply.Llama.Server;

namespace LMSupply.Generator.Internal.Llama;

/// <summary>
/// GGUF model implementation using llama-server (standalone llama.cpp HTTP server).
/// Uses LlamaServerPool for server instance reuse across model loads.
/// </summary>
internal sealed class LlamaServerGeneratorModel : IGeneratorModel
{
    private readonly ServerLease _serverLease;
    private readonly IChatFormatter _chatFormatter;
    private readonly GeneratorOptions _options;
    private readonly string _modelPath;
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly GgufMetadata? _ggufMetadata;
    private readonly string _serverVersion;
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

        // 2. Read GGUF metadata (best effort)
        GgufMetadata? ggufMetadata = null;
        try
        {
            ggufMetadata = await GgufMetadataReader.ReadAsync(modelPath, false, cancellationToken);
        }
        catch
        {
            // Ignore metadata reading errors
        }

        // 3. Configure and start llama-server
        progress?.Report(new DownloadProgress
        {
            FileName = Path.GetFileName(modelPath),
            BytesDownloaded = 30,
            TotalBytes = 100,
            Phase = DownloadPhase.Extracting
        });

        var llamaOpts = options.LlamaOptions ?? LlamaOptions.GetOptimalForHardware();
        var contextLength = options.MaxContextLength ?? 4096;

        // Build additional arguments
        var additionalArgs = new List<string>();
        if (llamaOpts.Threads.HasValue)
        {
            additionalArgs.Add("--threads");
            additionalArgs.Add(llamaOpts.Threads.Value.ToString(CultureInfo.InvariantCulture));
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
            CacheTypeK = MapKvCacheType(llamaOpts.TypeK),
            CacheTypeV = MapKvCacheType(llamaOpts.TypeV),
            // Phase 1: Memory options
            UseMemoryMap = llamaOpts.UseMemoryMap,
            UseMemoryLock = llamaOpts.UseMemoryLock,
            // Phase 1: GPU options
            MainGpu = llamaOpts.MainGpu,
            // Phase 1: RoPE options
            RopeFreqBase = llamaOpts.RopeFrequencyBase,
            RopeFreqScale = llamaOpts.RopeFrequencyScale,
            // Phase 3: Multimodal support
            MultimodalProjector = llamaOpts.MultimodalProjector,
            // Phase 3: LoRA support
            LoraPath = llamaOpts.LoraPath,
            LoraScale = llamaOpts.LoraScale,
            StartupTimeout = TimeSpan.FromSeconds(120),
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            AdditionalArgs = additionalArgs.Count > 0 ? additionalArgs : null
        };

        // 4. Lease server from pool (reuses existing server if available)
        var serverLease = await LlamaServerPool.Instance.LeaseAsync(
            serverPath,
            serverConfig,
            backend,
            progress,
            cancellationToken);

        progress?.Report(new DownloadProgress
        {
            FileName = Path.GetFileName(modelPath),
            BytesDownloaded = 100,
            TotalBytes = 100,
            Phase = DownloadPhase.Complete
        });

        // Extract server version from update result
        var serverVersion = updateResult.NewVersion ?? updateResult.PreviousVersion ?? "unknown";

        return new LlamaServerGeneratorModel(
            modelId,
            modelPath,
            serverLease,
            chatFormatter,
            options,
            contextLength,
            ggufMetadata,
            serverVersion);
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

            // Initialize reasoning token filter if needed
            var useReasoningFilter = options.FilterReasoningTokens || options.ExtractReasoningTokens;
            var reasoningFilter = useReasoningFilter
                ? new ReasoningTokenFilter(options.ExtractReasoningTokens)
                : null;

            await foreach (var token in _serverLease.Client.GenerateAsync(prompt, completionOptions, cancellationToken))
            {
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
            // Convert to llama-server format
            var serverMessages = messages.Select(m => new ChatCompletionMessage
            {
                Role = m.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.User => "user",
                    ChatRole.Assistant => "assistant",
                    _ => "user"
                },
                Content = m.Content
            });

            var chatOptions = new ChatCompletionOptions
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

            // Initialize reasoning token filter if needed
            var useReasoningFilter = options.FilterReasoningTokens || options.ExtractReasoningTokens;
            var reasoningFilter = useReasoningFilter
                ? new ReasoningTokenFilter(options.ExtractReasoningTokens)
                : null;

            await foreach (var token in _serverLease.Client.GenerateChatAsync(serverMessages, chatOptions, cancellationToken))
            {
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
        RuntimeVersion = _serverVersion
    };

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
    /// Maps KV cache quantization type to llama-server CLI format.
    /// </summary>
    private static string? MapKvCacheType(KvCacheQuantizationType? type)
    {
        return type switch
        {
            KvCacheQuantizationType.F16 => "f16",
            KvCacheQuantizationType.Q8_0 => "q8_0",
            KvCacheQuantizationType.Q4_0 => "q4_0",
            KvCacheQuantizationType.F32 => "f32",
            null => null,
            _ => null
        };
    }

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
