using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using LMSupply.Download;
using LMSupply.Exceptions;
using LMSupply.Generator.Abstractions;
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

        // 1b. Validate server version meets model requirements
        LlamaServerVersionRequirements.Validate(
            updateResult.NewVersion ?? updateResult.PreviousVersion, chatFormatter.FormatName);

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
            // Convert to llama-server format
            var augmentedMessages = MaybeInjectToolPromptFragment(messages, options.Tools, _chatFormatter);
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
            var augmentedMessages = MaybeInjectToolPromptFragment(messages, options.Tools, _chatFormatter);
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

            await foreach (var data in _serverLease.Client.GenerateChatStreamAsync(
                serverMessages, chatOptions, cancellationToken))
            {
                // Safety net: stop if token limit exceeded (finish_reason chunks still pass through)
                if (data.TextDelta is not null && maxTokens > 0 && ++tokenCount > maxTokens)
                {
                    yield return new ChatStreamChunk { FinishReason = "length" };
                    break;
                }

                // Convert tool call deltas from server types to Generator types
                IReadOnlyList<ChatToolCallDelta>? toolCallDeltas = null;
                if (data.ToolCallDeltas is { Count: > 0 })
                {
                    toolCallDeltas = data.ToolCallDeltas.Select(tc => new ChatToolCallDelta
                    {
                        Index = tc.Index,
                        Id = tc.Id,
                        Name = tc.Function?.Name,
                        Arguments = tc.Function?.Arguments
                    }).ToList();
                }

                // Apply reasoning filter to text delta
                var text = data.TextDelta;
                if (text is not null && reasoningFilter is not null)
                {
                    text = reasoningFilter.Process(text);
                    if (string.IsNullOrEmpty(text))
                        text = null;
                }

                // Yield structured chunk
                if (text is not null || toolCallDeltas is not null || data.FinishReason is not null)
                {
                    yield return new ChatStreamChunk
                    {
                        Text = text,
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
                    yield return new ChatStreamChunk { Text = remaining };
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
            var augmentedMessages = MaybeInjectToolPromptFragment(messages, options.Tools, _chatFormatter);
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
    internal static IEnumerable<ChatMessage> MaybeInjectToolPromptFragment(
        IEnumerable<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition>? tools,
        IChatFormatter formatter)
    {
        var fragment = formatter.RenderToolPromptFragment(tools);
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
