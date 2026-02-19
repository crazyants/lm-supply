using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using ChildProcessGuard;

namespace LMSupply.Llama.Server;

/// <summary>
/// Server operation mode - determines which llama-server endpoints are available.
/// </summary>
public enum ServerMode
{
    /// <summary>
    /// Text generation mode (default). Uses /completion and /v1/chat/completions endpoints.
    /// </summary>
    Generation,

    /// <summary>
    /// Embedding mode. Uses /v1/embeddings endpoint. Requires --embedding flag.
    /// </summary>
    Embedding,

    /// <summary>
    /// Reranking mode. Uses /v1/rerank endpoint. Requires --embedding and --pooling rank flags.
    /// </summary>
    Reranking
}

/// <summary>
/// Pooling type for embedding/reranking operations.
/// </summary>
public enum PoolingType
{
    /// <summary>
    /// No pooling specified (use model default).
    /// </summary>
    None,

    /// <summary>
    /// Mean pooling - average all token embeddings.
    /// </summary>
    Mean,

    /// <summary>
    /// CLS token pooling - use first token embedding.
    /// </summary>
    Cls,

    /// <summary>
    /// Last token pooling - use last token embedding.
    /// </summary>
    Last,

    /// <summary>
    /// Rank pooling - for reranking models (cross-encoder output).
    /// </summary>
    Rank
}

/// <summary>
/// Configuration for llama-server process.
/// </summary>
public sealed class LlamaServerConfig
{
    /// <summary>
    /// Path to the GGUF model file.
    /// </summary>
    public required string ModelPath { get; init; }

    /// <summary>
    /// Port to run the server on (0 for auto-assign).
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// Context size.
    /// </summary>
    public int ContextSize { get; init; } = 4096;

    /// <summary>
    /// Number of GPU layers to offload (-1 for all).
    /// </summary>
    public int GpuLayers { get; init; } = -1;

    /// <summary>
    /// Batch size for prompt processing (logical).
    /// Higher values speed up prompt evaluation but use more memory.
    /// </summary>
    public int BatchSize { get; init; } = 512;

    /// <summary>
    /// Physical batch size (ubatch). Controls VRAM usage during processing.
    /// Must be less than or equal to BatchSize. Default: 512.
    /// </summary>
    public int? UBatchSize { get; init; }

    /// <summary>
    /// Number of parallel sequences.
    /// </summary>
    public int Parallel { get; init; } = 1;

    /// <summary>
    /// Enable Flash Attention.
    /// </summary>
    public bool FlashAttention { get; init; }

    #region KV Cache Options

    /// <summary>
    /// KV cache type for keys (f16, q8_0, q4_0, f32).
    /// Reduces memory usage at the cost of potential quality loss.
    /// </summary>
    public string? CacheTypeK { get; init; }

    /// <summary>
    /// KV cache type for values (f16, q8_0, q4_0, f32).
    /// Reduces memory usage at the cost of potential quality loss.
    /// </summary>
    public string? CacheTypeV { get; init; }

    #endregion

    #region Memory Options

    /// <summary>
    /// Use memory mapping for model loading (mmap).
    /// Enables faster loading and sharing between processes. Default: true.
    /// </summary>
    public bool? UseMemoryMap { get; init; }

    /// <summary>
    /// Lock model memory to prevent swapping (mlock).
    /// Improves latency but may require elevated privileges.
    /// </summary>
    public bool? UseMemoryLock { get; init; }

    #endregion

    #region GPU Options

    /// <summary>
    /// Main GPU index for multi-GPU systems (0-based).
    /// </summary>
    public int? MainGpu { get; init; }

    #endregion

    #region RoPE Options

    /// <summary>
    /// RoPE frequency base for context extension.
    /// Use with RoPE-scaling-aware models.
    /// </summary>
    public float? RopeFreqBase { get; init; }

    /// <summary>
    /// RoPE frequency scale factor.
    /// Use with RoPE-scaling-aware models.
    /// </summary>
    public float? RopeFreqScale { get; init; }

    #endregion

    #region Multimodal Options (Phase 3)

    /// <summary>
    /// Path to multimodal projector file (--mmproj).
    /// Required for vision models like LLaVA.
    /// </summary>
    public string? MultimodalProjector { get; init; }

    #endregion

    #region LoRA Options (Phase 3)

    /// <summary>
    /// Path to LoRA adapter file (--lora).
    /// </summary>
    public string? LoraPath { get; init; }

    /// <summary>
    /// LoRA adapter scale (--lora-scaled).
    /// </summary>
    public float? LoraScale { get; init; }

    #endregion

    /// <summary>
    /// Additional command line arguments.
    /// </summary>
    public IReadOnlyList<string>? AdditionalArgs { get; init; }

    /// <summary>
    /// Server operation mode. Default: Generation.
    /// Embedding mode: enables --embedding flag
    /// Reranking mode: enables --embedding and --pooling rank
    /// </summary>
    public ServerMode Mode { get; init; } = ServerMode.Generation;

    /// <summary>
    /// Pooling type for embedding/reranking modes.
    /// Only applicable when Mode is Embedding or Reranking.
    /// </summary>
    public PoolingType Pooling { get; init; } = PoolingType.None;

    /// <summary>
    /// Timeout for server startup.
    /// </summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Timeout for graceful shutdown.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Information about a running llama-server process.
/// </summary>
public sealed record LlamaServerInfo
{
    /// <summary>
    /// Process ID.
    /// </summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// Port the server is listening on.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Base URL for API calls.
    /// </summary>
    public string BaseUrl => $"http://localhost:{Port}";

    /// <summary>
    /// Model path being served.
    /// </summary>
    public required string ModelPath { get; init; }

    /// <summary>
    /// llama-server version.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// GPU backend being used.
    /// </summary>
    public LlamaServerBackend Backend { get; init; }

    /// <summary>
    /// Time when server was started.
    /// </summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// Startup log from the server (stderr output during initialization).
    /// </summary>
    public string? StartupLog { get; init; }
}

/// <summary>
/// Manages llama-server process lifecycle with automatic cleanup.
/// </summary>
public sealed class LlamaServerProcess : IAsyncDisposable
{
    private readonly ProcessGuardian _guardian;
    private readonly LlamaServerConfig _config;
    private readonly string _serverPath;
    private readonly LlamaServerBackend _backend;
    private readonly HttpClient _httpClient;

    private Process? _process;
    private int _port;
    private bool _disposed;

    /// <summary>
    /// Gets information about the running server.
    /// </summary>
    public LlamaServerInfo? Info { get; private set; }

    /// <summary>
    /// Gets whether the server is running.
    /// </summary>
    public bool IsRunning => _process is { HasExited: false };

    private LlamaServerProcess(
        string serverPath,
        LlamaServerConfig config,
        LlamaServerBackend backend)
    {
        _serverPath = serverPath;
        _config = config;
        _backend = backend;

        _guardian = new ProcessGuardian(new ProcessGuardianOptions
        {
            ProcessKillTimeout = config.ShutdownTimeout
        });

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    /// <summary>
    /// Starts a new llama-server process.
    /// </summary>
    public static async Task<LlamaServerProcess> StartAsync(
        string serverPath,
        LlamaServerConfig config,
        LlamaServerBackend backend,
        CancellationToken cancellationToken = default)
    {
        var server = new LlamaServerProcess(serverPath, config, backend);

        try
        {
            await server.StartInternalAsync(cancellationToken);
            return server;
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    private async Task StartInternalAsync(CancellationToken cancellationToken)
    {
        // Find available port if not specified
        _port = _config.Port > 0 ? _config.Port : FindAvailablePort();

        // Build arguments
        var args = BuildArguments();

        // Get the directory containing llama-server for DLL resolution
        var workingDir = Path.GetDirectoryName(_serverPath)!;

        // Start process via guardian
        var startInfo = new ProcessStartInfo
        {
            FileName = _serverPath,
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Enable CUDA graph optimization for NVIDIA GPUs (reduces token generation latency)
        if (_backend == LlamaServerBackend.Cuda12 || _backend == LlamaServerBackend.Cuda13)
        {
            startInfo.Environment["GGML_CUDA_GRAPH_OPT"] = "1";
        }

        _process = _guardian.StartProcessWithStartInfo(startInfo);

        // Capture stderr output for diagnostics
        var stderrBuilder = new System.Text.StringBuilder();
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };
        _process.BeginErrorReadLine();

        // Wait for server to be ready
        var startTime = DateTimeOffset.UtcNow;
        var ready = await WaitForServerReadyAsync(_config.StartupTimeout, cancellationToken);

        if (!ready)
        {
            // Collect error output
            var error = stderrBuilder.ToString();
            if (string.IsNullOrEmpty(error) && _process.HasExited)
            {
                error = "Process exited without error output";
            }

            throw new InvalidOperationException(
                $"llama-server failed to start within {_config.StartupTimeout.TotalSeconds}s. " +
                $"Exit code: {(_process.HasExited ? _process.ExitCode : "still running")}. " +
                $"Error: {error}");
        }

        Info = new LlamaServerInfo
        {
            ProcessId = _process.Id,
            Port = _port,
            ModelPath = _config.ModelPath,
            Backend = _backend,
            StartTime = startTime,
            StartupLog = stderrBuilder.ToString()
        };
    }

    private string BuildArguments()
    {
        var args = new List<string>
        {
            "--model", $"\"{_config.ModelPath}\"",
            "--port", _port.ToString(CultureInfo.InvariantCulture),
            "--ctx-size", _config.ContextSize.ToString(CultureInfo.InvariantCulture),
            "--n-gpu-layers", _config.GpuLayers.ToString(CultureInfo.InvariantCulture),
            "--batch-size", _config.BatchSize.ToString(CultureInfo.InvariantCulture),
            "--parallel", _config.Parallel.ToString(CultureInfo.InvariantCulture),
            "--host", "127.0.0.1", // Only listen on localhost for security
            "--cont-batching"      // Enable continuous batching for better throughput
        };

        // Physical batch size for VRAM efficiency
        if (_config.UBatchSize.HasValue)
        {
            args.Add("--ubatch-size");
            args.Add(_config.UBatchSize.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (_config.FlashAttention)
        {
            args.Add("--flash-attn");
        }

        // KV cache quantization (Phase 1)
        if (!string.IsNullOrEmpty(_config.CacheTypeK))
        {
            args.Add("--cache-type-k");
            args.Add(_config.CacheTypeK);
        }

        if (!string.IsNullOrEmpty(_config.CacheTypeV))
        {
            args.Add("--cache-type-v");
            args.Add(_config.CacheTypeV);
        }

        // Memory options (Phase 1)
        if (_config.UseMemoryMap.HasValue)
        {
            args.Add(_config.UseMemoryMap.Value ? "--mmap" : "--no-mmap");
        }

        if (_config.UseMemoryLock == true)
        {
            args.Add("--mlock");
        }

        // GPU options (Phase 1)
        if (_config.MainGpu.HasValue)
        {
            args.Add("--main-gpu");
            args.Add(_config.MainGpu.Value.ToString(CultureInfo.InvariantCulture));
        }

        // RoPE options (Phase 1)
        if (_config.RopeFreqBase.HasValue)
        {
            args.Add("--rope-freq-base");
            args.Add(_config.RopeFreqBase.Value.ToString("F1", CultureInfo.InvariantCulture));
        }

        if (_config.RopeFreqScale.HasValue)
        {
            args.Add("--rope-freq-scale");
            args.Add(_config.RopeFreqScale.Value.ToString("F4", CultureInfo.InvariantCulture));
        }

        // Multimodal projector (Phase 3)
        if (!string.IsNullOrEmpty(_config.MultimodalProjector))
        {
            args.Add("--mmproj");
            args.Add($"\"{_config.MultimodalProjector}\"");
        }

        // LoRA adapter (Phase 3)
        if (!string.IsNullOrEmpty(_config.LoraPath))
        {
            if (_config.LoraScale.HasValue)
            {
                args.Add("--lora-scaled");
                args.Add($"\"{_config.LoraPath}\"");
                args.Add(_config.LoraScale.Value.ToString("F2", CultureInfo.InvariantCulture));
            }
            else
            {
                args.Add("--lora");
                args.Add($"\"{_config.LoraPath}\"");
            }
        }

        // Embedding/Reranking mode flags
        if (_config.Mode == ServerMode.Embedding || _config.Mode == ServerMode.Reranking)
        {
            args.Add("--embedding");
        }

        // Pooling type
        var poolingType = _config.Pooling;

        // For reranking mode, force rank pooling if not explicitly set
        if (_config.Mode == ServerMode.Reranking && poolingType == PoolingType.None)
        {
            poolingType = PoolingType.Rank;
        }

        if (poolingType != PoolingType.None)
        {
            args.Add("--pooling");
            args.Add(poolingType.ToString().ToLowerInvariant());
        }

        if (_config.AdditionalArgs != null)
        {
            args.AddRange(_config.AdditionalArgs);
        }

        return string.Join(" ", args);
    }

    private async Task<bool> WaitForServerReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_process?.HasExited == true)
            {
                return false;
            }

            try
            {
                var response = await _httpClient.GetAsync($"http://localhost:{_port}/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Server not ready yet
            }
            catch (TaskCanceledException)
            {
                // Timeout, retry
            }

            await Task.Delay(100, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Checks if the server is healthy.
    /// </summary>
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
            return false;

        try
        {
            var response = await _httpClient.GetAsync($"http://localhost:{_port}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stops the server gracefully.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_process == null || _process.HasExited)
            return;

        try
        {
            // Try graceful shutdown first
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
    }

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await StopAsync();
        _httpClient.Dispose();
        _guardian.Dispose();
        _process?.Dispose();
    }
}
