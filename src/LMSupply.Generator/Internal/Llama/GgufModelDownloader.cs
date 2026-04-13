using System.Diagnostics;
using LMSupply.Core.Download;
using LMSupply.Hardware;
using LMSupply.Download;
using LMSupply.Exceptions;

namespace LMSupply.Generator.Internal.Llama;

/// <summary>
/// Downloads GGUF model files from HuggingFace with automatic quantization selection.
/// </summary>
public sealed class GgufModelDownloader : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ModelDiscoveryService _discoveryService;
    private readonly string _cacheDirectory;
    private bool _disposed;

    private const string HuggingFaceFileBase = "https://huggingface.co";

    /// <summary>
    /// Default quantization preference order (best balance of quality vs size first).
    /// </summary>
    private static readonly string[] DefaultQuantizationPriority =
    [
        "Q4_K_M", "Q4_K_S", "Q5_K_M", "Q5_K_S",
        "Q6_K", "Q8_0", "Q3_K_M", "Q3_K_L", "Q2_K",
        "IQ4_XS", "IQ4_NL"
    ];

    public GgufModelDownloader() : this(null)
    {
    }

    public GgufModelDownloader(string? cacheDirectory)
    {
        _cacheDirectory = cacheDirectory ?? CacheManager.GetDefaultCacheDirectory();
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LMSupply/1.0");
        _discoveryService = new ModelDiscoveryService(_cacheDirectory);
    }

    /// <summary>
    /// Downloads a GGUF model file from HuggingFace.
    /// </summary>
    /// <param name="repoId">HuggingFace repository ID (e.g., "bartowski/Llama-3.2-3B-Instruct-GGUF").</param>
    /// <param name="filename">Specific file to download. If null, auto-selects based on quantization.</param>
    /// <param name="preferredQuantization">Preferred quantization (e.g., "Q4_K_M"). If null, uses default priority.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the downloaded GGUF file.</returns>
    public async Task<string> DownloadAsync(
        string repoId,
        string? filename = null,
        string? preferredQuantization = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);

        // Determine the file to download
        if (string.IsNullOrEmpty(filename))
        {
            filename = await SelectBestGgufFileAsync(repoId, preferredQuantization, cancellationToken);
        }

        // Check cache
        var cachedPath = GetCachedPath(repoId, filename);
        if (File.Exists(cachedPath))
        {
            progress?.Report(new DownloadProgress
            {
                FileName = filename,
                BytesDownloaded = 1,
                TotalBytes = 1
            });
            return cachedPath;
        }

        // Download the file
        progress?.Report(new DownloadProgress
        {
            FileName = filename,
            BytesDownloaded = 0,
            TotalBytes = 0
        });

        await DownloadFileAsync(repoId, filename, cachedPath, progress, cancellationToken);

        return cachedPath;
    }

    /// <summary>
    /// Downloads a model using registry information.
    /// </summary>
    public async Task<string> DownloadFromRegistryAsync(
        GgufModelInfo modelInfo,
        string? preferredQuantization = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Use registry default file unless different quantization is preferred
        var filename = modelInfo.DefaultFile;

        if (!string.IsNullOrEmpty(preferredQuantization) &&
            !modelInfo.DefaultFile.Contains(preferredQuantization, StringComparison.OrdinalIgnoreCase))
        {
            // Try to find a file with preferred quantization
            var alternateFile = await TryFindQuantizedFileAsync(
                modelInfo.RepoId, preferredQuantization, cancellationToken);

            if (alternateFile != null)
            {
                filename = alternateFile;
            }
        }

        // Handle split GGUF models (multiple shards)
        if (modelInfo.ShardCount is > 1)
        {
            return await DownloadSplitModelAsync(
                modelInfo.RepoId, filename, modelInfo.ShardCount.Value,
                progress, cancellationToken);
        }

        return await DownloadAsync(modelInfo.RepoId, filename, preferredQuantization, progress, cancellationToken);
    }

    /// <summary>
    /// Downloads all shards of a split GGUF model.
    /// Returns the path to the first shard (llama-server auto-loads the rest).
    /// </summary>
    private async Task<string> DownloadSplitModelAsync(
        string repoId,
        string firstShardFilename,
        int shardCount,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var shardFilenames = GenerateShardFilenames(firstShardFilename, shardCount);
        string? firstShardPath = null;

        for (int i = 0; i < shardFilenames.Count; i++)
        {
            var shardFile = shardFilenames[i];
            progress?.Report(new DownloadProgress
            {
                FileName = $"{Path.GetFileName(shardFile)} ({i + 1}/{shardCount})",
                BytesDownloaded = 0,
                TotalBytes = 0
            });

            var path = await DownloadAsync(repoId, shardFile, preferredQuantization: null,
                progress, cancellationToken);

            firstShardPath ??= path;
        }

        return firstShardPath!;
    }

    /// <summary>
    /// Generates all shard filenames from the first shard filename.
    /// E.g., "Q4_K_M/model-00001-of-00003.gguf" → ["...00001...", "...00002...", "...00003..."]
    /// </summary>
    internal static IReadOnlyList<string> GenerateShardFilenames(string firstShardFilename, int shardCount)
    {
        var filenames = new List<string>(shardCount);

        // Find the shard number pattern in the filename
        var match = System.Text.RegularExpressions.Regex.Match(
            firstShardFilename, @"-(\d{5})-of-(\d{5})\.gguf$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            // Not a split pattern — return as single file
            return [firstShardFilename];
        }

        var prefix = firstShardFilename[..match.Index];
        var totalStr = match.Groups[2].Value;

        for (int i = 1; i <= shardCount; i++)
        {
            filenames.Add($"{prefix}-{i:D5}-of-{totalStr}.gguf");
        }

        return filenames;
    }

    /// <summary>
    /// Lists available GGUF files in a repository.
    /// </summary>
    public async Task<IReadOnlyList<GgufFileInfo>> ListGgufFilesAsync(
        string repoId,
        CancellationToken cancellationToken = default)
    {
        var groups = await ListGgufGroupsAsync(repoId, cancellationToken);

        return groups.Select(g => new GgufFileInfo
        {
            FileName = g.PrimaryFileName,
            Path = g.PrimaryFileName,
            SizeBytes = g.TotalSizeBytes,
            Quantization = null
        }).ToList();
    }

    /// <summary>
    /// Lists available GGUF files in a repository, grouped for split-file support.
    /// </summary>
    public async Task<IReadOnlyList<GgufFileGroup>> ListGgufGroupsAsync(
        string repoId,
        CancellationToken cancellationToken = default)
    {
        var files = await ListRepositoryFilesAsync(repoId, cancellationToken);

        var rawFiles = files
            .Where(f => f.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            .Select(f => new GgufRawFile(Path.GetFileName(f.Path), f.Size))
            .ToList();

        return GgufFileGroup.GroupFiles(rawFiles).ToList();
    }

    /// <summary>
    /// Selects the best GGUF file based on hardware memory constraints and quantization preference.
    /// </summary>
    private async Task<string> SelectBestGgufFileAsync(
        string repoId,
        string? preferredQuantization,
        CancellationToken cancellationToken)
    {
        var groups = await ListGgufGroupsAsync(repoId, cancellationToken);

        if (groups.Count == 0)
            throw new ModelNotFoundException(
                $"No GGUF files found in repository '{repoId}'.",
                repoId);

        var memory = GgufFileSelector.FromHardwareProfile(HardwareProfile.Current);
        var selected = GgufFileSelector.Select(groups, memory, preferredQuantization);
        return selected.PrimaryFileName;
    }

    /// <summary>
    /// Tries to find a file with specific quantization.
    /// </summary>
    private async Task<string?> TryFindQuantizedFileAsync(
        string repoId,
        string quantization,
        CancellationToken cancellationToken)
    {
        try
        {
            var files = await ListGgufFilesAsync(repoId, cancellationToken);
            var match = files.FirstOrDefault(f =>
                f.FileName.Contains(quantization, StringComparison.OrdinalIgnoreCase));

            return match?.FileName;
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"[GgufModelDownloader] GGUF file search failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Lists all files in a HuggingFace repository using the Tree API (includes file sizes).
    /// </summary>
    private Task<IReadOnlyList<RepoFile>> ListRepositoryFilesAsync(
        string repoId,
        CancellationToken cancellationToken)
    {
        return _discoveryService.ListRepositoryFilesAsync(repoId, "main", cancellationToken);
    }

    /// <summary>
    /// Downloads a single file with resume support.
    /// </summary>
    private async Task DownloadFileAsync(
        string repoId,
        string filename,
        string destinationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Ensure directory exists
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Build download URL
        var url = $"{HuggingFaceFileBase}/{repoId}/resolve/main/{filename}";

        var tempPath = destinationPath + ".part";
        long startPosition = 0;

        // Check for partial download
        if (File.Exists(tempPath))
        {
            startPosition = new FileInfo(tempPath).Length;
        }

        // Create request with optional range header
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (startPosition > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startPosition, null);
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        // Handle 416 (Range Not Satisfiable) - file already complete
        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            if (File.Exists(tempPath))
            {
                File.Move(tempPath, destinationPath, overwrite: true);
            }
            return;
        }

        response.EnsureSuccessStatusCode();

        // Determine total size
        long totalBytes = response.Content.Headers.ContentLength ?? 0;
        if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
        {
            var contentRange = response.Content.Headers.ContentRange;
            if (contentRange?.Length.HasValue == true)
            {
                totalBytes = contentRange.Length.Value;
            }
            else
            {
                totalBytes = startPosition + (response.Content.Headers.ContentLength ?? 0);
            }
        }
        else
        {
            startPosition = 0; // Full download
        }

        // Download with progress - use explicit block to ensure streams are closed before File.Move
        {
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var fileMode = startPosition > 0 ? FileMode.Append : FileMode.Create;
            await using var fileStream = new FileStream(tempPath, fileMode, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long bytesDownloaded = startPosition;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                bytesDownloaded += bytesRead;

                progress?.Report(new DownloadProgress
                {
                    FileName = filename,
                    BytesDownloaded = bytesDownloaded,
                    TotalBytes = totalBytes
                });
            }

            // Ensure data is flushed to disk
            await fileStream.FlushAsync(cancellationToken);
        }

        // Move to final location (streams are now closed)
        File.Move(tempPath, destinationPath, overwrite: true);
    }

    private string GetCachedPath(string repoId, string filename)
    {
        // Store in HuggingFace-compatible structure
        var safeRepoId = repoId.Replace('/', Path.DirectorySeparatorChar);
        var modelDir = "models--" + safeRepoId.Replace(Path.DirectorySeparatorChar.ToString(), "--");
        return Path.Combine(_cacheDirectory, modelDir, "snapshots", "main", filename);
    }

    private static string? ExtractQuantization(string filename)
    {
        // Common GGUF quantization patterns
        var quantPatterns = new[]
        {
            "Q2_K", "Q3_K_S", "Q3_K_M", "Q3_K_L",
            "Q4_K_S", "Q4_K_M", "Q4_0", "Q4_1",
            "Q5_K_S", "Q5_K_M", "Q5_0", "Q5_1",
            "Q6_K", "Q8_0", "F16", "F32",
            "IQ4_XS", "IQ4_NL", "IQ3_XXS", "IQ3_XS"
        };

        foreach (var pattern in quantPatterns)
        {
            if (filename.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return pattern;
        }

        return null;
    }

    private static int GetQuantizationPriority(string? quantization)
    {
        if (string.IsNullOrEmpty(quantization))
            return -1;

        var index = Array.FindIndex(DefaultQuantizationPriority,
            q => q.Equals(quantization, StringComparison.OrdinalIgnoreCase));

        return index >= 0 ? DefaultQuantizationPriority.Length - index : -1;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _discoveryService.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Information about a GGUF file in a repository.
/// </summary>
public sealed record GgufFileInfo
{
    /// <summary>File name (e.g., "Llama-3.2-3B-Instruct-Q4_K_M.gguf").</summary>
    public required string FileName { get; init; }

    /// <summary>Full path within repository.</summary>
    public required string Path { get; init; }

    /// <summary>File size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Detected quantization type (e.g., "Q4_K_M").</summary>
    public string? Quantization { get; init; }

    /// <summary>File size in gigabytes.</summary>
    public double SizeGB => SizeBytes / (1024.0 * 1024.0 * 1024.0);
}
