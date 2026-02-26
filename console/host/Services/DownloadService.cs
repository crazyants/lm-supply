using LMSupply.Core.Download;
using LMSupply.Download;

namespace LMSupply.Console.Host.Services;

/// <summary>
/// Model download service (thin wrapper over HuggingFaceDownloader).
/// </summary>
public sealed partial class DownloadService : IDisposable
{
    private readonly HuggingFaceDownloader _downloader;
    private readonly ModelDiscoveryService _discoveryService;
    private readonly ILogger<DownloadService> _logger;
    private bool _disposed;

    public DownloadService(CacheService cacheService, ILogger<DownloadService> logger)
    {
        _downloader = new HuggingFaceDownloader(cacheService.CacheDirectory);
        _discoveryService = new ModelDiscoveryService(cacheService.CacheDirectory);
        _logger = logger;
    }

    /// <summary>
    /// Checks if a model exists on HuggingFace and returns its information.
    /// </summary>
    public async Task<ModelCheckResult> CheckModelAsync(string repoId, CancellationToken cancellationToken = default)
    {
        try
        {
            var files = await _discoveryService.ListRepositoryFilesAsync(repoId, "main", cancellationToken);

            // Detect model type based on files
            var fileNames = files.Select(f => f.FileName).ToList();
            var detectedType = CacheManager.DetectModelType(fileNames, repoId);

            // Calculate total size
            var totalSize = files.Sum(f => f.Size);

            // Check for GGUF files (provide selection options)
            var hasOnnx = files.Any(f => f.Path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
            List<GgufFileInfo>? ggufFileInfos = null;
            if (!hasOnnx)
            {
                var ggufFiles = files
                    .Where(f => f.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Size)
                    .ToList();
                if (ggufFiles.Count > 0)
                {
                    ggufFileInfos = ggufFiles
                        .Select(f => new GgufFileInfo { FileName = f.Path, SizeBytes = f.Size })
                        .ToList();
                }
            }

            return new ModelCheckResult
            {
                Exists = true,
                RepoId = repoId,
                DetectedType = detectedType,
                FileCount = files.Count,
                TotalSizeBytes = totalSize,
                Files = fileNames,
                GgufFiles = ggufFileInfos
            };
        }
        catch (Exception ex)
        {
            LogModelCheckFailed(_logger, ex, repoId);
            return new ModelCheckResult
            {
                Exists = false,
                RepoId = repoId,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Downloads a model with async progress reporting via callback.
    /// Supports both ONNX and GGUF model formats.
    /// </summary>
    /// <param name="repoId">HuggingFace repository ID.</param>
    /// <param name="onProgressAsync">Progress callback.</param>
    /// <param name="fileName">Optional GGUF file name to download. If null, auto-selects based on system specs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DownloadModelAsync(
        string repoId,
        Func<DownloadProgress, Task> onProgressAsync,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        LogDownloadStarting(_logger, repoId);

        // Use AsyncProgress with cancellation token to properly handle async callbacks
        var progress = new AsyncProgress<DownloadProgress>(onProgressAsync, cancellationToken);

        try
        {
            // Pre-check repository files to determine format
            var allFiles = await _discoveryService.ListRepositoryFilesAsync(repoId, "main", cancellationToken);
            var hasOnnx = allFiles.Any(f => f.Path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
            var ggufFiles = allFiles
                .Where(f => f.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!hasOnnx && ggufFiles.Count > 0)
            {
                // GGUF-only repository: select file and download with configs
                await DownloadGgufModelAsync(repoId, allFiles, ggufFiles, fileName, progress, cancellationToken);
            }
            else
            {
                // ONNX repository: use standard discovery-based download
                await _downloader.DownloadWithDiscoveryAsync(
                    repoId,
                    preferences: null,
                    revision: "main",
                    progress: progress,
                    cancellationToken: cancellationToken);
            }

            LogDownloadCompleted(_logger, repoId);
        }
        catch (Exception ex)
        {
            LogDownloadFailed(_logger, ex, repoId);
            throw;
        }
    }

    /// <summary>
    /// Config file names to download alongside GGUF models.
    /// </summary>
    private static readonly HashSet<string> GgufConfigFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "config.json", "tokenizer.json", "tokenizer_config.json",
        "vocab.json", "vocab.txt", "merges.txt",
        "special_tokens_map.json", "sentencepiece.bpe.model",
        "generation_config.json", "preprocessor_config.json"
    };

    /// <summary>
    /// Downloads a GGUF model file plus config files.
    /// If fileName is specified, downloads that exact file.
    /// Otherwise, auto-selects based on system RAM/VRAM.
    /// </summary>
    private async Task DownloadGgufModelAsync(
        string repoId,
        IReadOnlyList<RepoFile> allFiles,
        List<RepoFile> ggufFiles,
        string? fileName,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        RepoFile? selected = null;

        if (!string.IsNullOrEmpty(fileName))
        {
            // User explicitly selected a file
            selected = ggufFiles.FirstOrDefault(f =>
                f.Path.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }

        // Auto-select based on system specs
        selected ??= SelectGgufBySystemSpec(ggufFiles);

        var modelDir = CacheManager.GetModelDirectory(_downloader.CacheDirectory, repoId, "main");
        Directory.CreateDirectory(modelDir);

        // Download the GGUF file
        var ggufLocalPath = Path.Combine(modelDir, selected.Path.Replace('/', Path.DirectorySeparatorChar));
        var ggufParentDir = Path.GetDirectoryName(ggufLocalPath);
        if (!string.IsNullOrEmpty(ggufParentDir))
            Directory.CreateDirectory(ggufParentDir);

        if (!File.Exists(ggufLocalPath) || CacheManager.IsLfsPointerFile(ggufLocalPath))
        {
            await _downloader.DownloadFileAsync(
                repoId, selected.Path, ggufLocalPath,
                revision: "main", subfolder: null,
                progress: progress, cancellationToken: cancellationToken);
        }

        // Download config/tokenizer files from root
        foreach (var file in allFiles)
        {
            if (!file.IsFile) continue;
            if (file.Directory is not null) continue; // root-level only
            if (!GgufConfigFileNames.Contains(file.FileName)) continue;

            var localPath = Path.Combine(modelDir, file.Path);
            if (!File.Exists(localPath))
            {
                try
                {
                    await _downloader.DownloadFileAsync(
                        repoId, file.Path, localPath,
                        revision: "main", subfolder: null,
                        progress: progress, cancellationToken: cancellationToken);
                }
                catch (HttpRequestException)
                {
                    // Non-critical config file - skip if not found
                }
            }
        }
    }

    /// <summary>
    /// Selects the best GGUF file based on available system RAM/VRAM.
    /// Strategy: pick the largest quantization that fits comfortably in memory.
    /// </summary>
    private static RepoFile SelectGgufBySystemSpec(List<RepoFile> ggufFiles)
    {
        // Get available memory (GGUF models are loaded into RAM, or VRAM if GPU offloading)
        var availableBytes = GetAvailableMemoryBytes();

        if (availableBytes > 0)
        {
            // Allow using up to ~70% of available memory for the model
            var budget = (long)(availableBytes * 0.7);

            // Prefer the largest file that fits within budget (higher quality)
            var fits = ggufFiles
                .Where(f => f.Size <= budget)
                .OrderByDescending(f => f.Size)
                .FirstOrDefault();

            if (fits is not null)
                return fits;
        }

        // Fallback: use static quantization priority
        string[] priority = ["Q4_K_M", "Q4_K_S", "Q5_K_M", "Q5_K_S", "Q8_0", "F16"];
        foreach (var quant in priority)
        {
            var match = ggufFiles.FirstOrDefault(f =>
                f.Path.Contains(quant, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        // Last resort: smallest file
        return ggufFiles.OrderBy(f => f.Size).First();
    }

    /// <summary>
    /// Gets total system memory in bytes for GGUF file sizing decisions.
    /// </summary>
    private static long GetAvailableMemoryBytes()
    {
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            return gcInfo.TotalAvailableMemoryBytes;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _downloader.Dispose();
        _discoveryService.Dispose();
        _disposed = true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model check failed for {RepoId}")]
    private static partial void LogModelCheckFailed(ILogger logger, Exception exception, string repoId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting download: {RepoId}")]
    private static partial void LogDownloadStarting(ILogger logger, string repoId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Download completed: {RepoId}")]
    private static partial void LogDownloadCompleted(ILogger logger, string repoId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Download failed: {RepoId}")]
    private static partial void LogDownloadFailed(ILogger logger, Exception exception, string repoId);
}

/// <summary>
/// Thread-safe async progress reporter that serializes async callbacks.
/// </summary>
internal sealed class AsyncProgress<T> : IProgress<T>, IDisposable
{
    private readonly Func<T, Task> _handler;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationToken _cancellationToken;

    public AsyncProgress(Func<T, Task> handler, CancellationToken cancellationToken = default)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _cancellationToken = cancellationToken;
    }

    public void Dispose() => _writeLock.Dispose();

    public void Report(T value)
    {
        if (_cancellationToken.IsCancellationRequested)
            return;

        // Synchronously wait for lock then execute async handler
        // This ensures writes are serialized and not interleaved
        try
        {
            _writeLock.Wait(_cancellationToken);
            try
            {
                _handler(value).GetAwaiter().GetResult();
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled - ignore
        }
        catch
        {
            // Swallow exceptions in progress reporting to avoid disrupting download
        }
    }
}

/// <summary>
/// Result of model validation check.
/// </summary>
public record ModelCheckResult
{
    public bool Exists { get; init; }
    public required string RepoId { get; init; }
    public ModelType DetectedType { get; init; }
    public int FileCount { get; init; }
    public long TotalSizeBytes { get; init; }
    public double TotalSizeMB => TotalSizeBytes / (1024.0 * 1024.0);
    public IReadOnlyList<string> Files { get; init; } = [];
    /// <summary>
    /// GGUF files available for selection (only populated for GGUF-only repositories).
    /// </summary>
    public IReadOnlyList<GgufFileInfo>? GgufFiles { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Information about a GGUF file in a repository.
/// </summary>
public record GgufFileInfo
{
    public required string FileName { get; init; }
    public long SizeBytes { get; init; }
    public double SizeMB => SizeBytes / (1024.0 * 1024.0);
}
