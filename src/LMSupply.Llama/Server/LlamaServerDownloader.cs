using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LMSupply.Runtime;

namespace LMSupply.Llama.Server;

/// <summary>
/// Downloads llama-server binaries from GitHub releases.
/// </summary>
public sealed class LlamaServerDownloader : IDisposable
{
    private const string GitHubApiBase = "https://api.github.com/repos/ggml-org/llama.cpp";
    private const string ReleasesUrl = $"{GitHubApiBase}/releases";

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a new downloader instance.
    /// </summary>
    /// <param name="cacheDirectory">Directory to store downloaded binaries.</param>
    /// <param name="httpClient">Optional HTTP client (creates new if null).</param>
    public LlamaServerDownloader(string? cacheDirectory = null, HttpClient? httpClient = null)
    {
        _cacheDirectory = cacheDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LMSupply", "cache", "llama-server");

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "LMSupply");
            _ownsHttpClient = true;
        }
    }

    /// <summary>
    /// Gets the latest release version.
    /// </summary>
    public async Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{ReleasesUrl}/latest", cancellationToken);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            return doc.RootElement.GetProperty("tag_name").GetString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the available asset for the current platform and preferred backend.
    /// </summary>
    public async Task<LlamaServerAsset?> GetAssetAsync(
        string? version = null,
        LlamaServerBackend? preferredBackend = null,
        CancellationToken cancellationToken = default)
    {
        version ??= await GetLatestVersionAsync(cancellationToken);
        if (version == null)
            return null;

        var platform = GetCurrentPlatform();
        var arch = GetCurrentArchitecture();
        var backend = preferredBackend ?? GetPreferredBackend(platform);

        // Get release assets
        var releaseUrl = $"{ReleasesUrl}/tags/{version}";
        var response = await _httpClient.GetAsync(releaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        var assets = doc.RootElement.GetProperty("assets");

        // Find matching asset
        var assetPattern = GetAssetPattern(platform, arch, backend);

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name != null && assetPattern.IsMatch(name))
            {
                return new LlamaServerAsset
                {
                    Name = name,
                    DownloadUrl = asset.GetProperty("browser_download_url").GetString()!,
                    Version = version,
                    Platform = platform,
                    Backend = backend,
                    Architecture = arch,
                    SizeBytes = asset.TryGetProperty("size", out var size) ? size.GetInt64() : null
                };
            }
        }

        // Fallback to CPU if preferred backend not found
        if (backend != LlamaServerBackend.Cpu)
        {
            return await GetAssetAsync(version, LlamaServerBackend.Cpu, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Downloads and extracts llama-server to the cache directory.
    /// Returns the path to the llama-server executable.
    /// </summary>
    public async Task<string> DownloadAsync(
        LlamaServerAsset asset,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var versionDir = Path.Combine(_cacheDirectory, asset.Version, asset.Backend.ToString().ToLowerInvariant());
        var serverPath = GetServerExecutablePath(versionDir, asset.Platform);

        // Check if already downloaded
        if (File.Exists(serverPath))
        {
            return serverPath;
        }

        Directory.CreateDirectory(versionDir);

        // Download archive
        var archivePath = Path.Combine(versionDir, asset.Name);

        progress?.Report(new DownloadProgress
        {
            FileName = asset.Name,
            BytesDownloaded = 0,
            TotalBytes = asset.SizeBytes ?? 0,
            Phase = DownloadPhase.Downloading
        });

        using (var response = await _httpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? asset.SizeBytes ?? 0;
            var tracker = new DownloadProgressTracker();
            tracker.Start();

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = File.Create(archivePath);

            var buffer = new byte[81920];
            long bytesDownloaded = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                bytesDownloaded += bytesRead;

                progress?.Report(tracker.CreateProgress(asset.Name, bytesDownloaded, totalBytes, DownloadPhase.Downloading));
            }
        }

        // Extract archive
        progress?.Report(new DownloadProgress
        {
            FileName = asset.Name,
            BytesDownloaded = 0,
            TotalBytes = 0,
            Phase = DownloadPhase.Extracting
        });

        await ExtractArchiveAsync(archivePath, versionDir, asset.Platform, cancellationToken);

        // Cleanup archive
        File.Delete(archivePath);

        progress?.Report(new DownloadProgress
        {
            FileName = asset.Name,
            BytesDownloaded = 100,
            TotalBytes = 100,
            Phase = DownloadPhase.Complete
        });

        return serverPath;
    }

    /// <summary>
    /// Ensures llama-server is available, downloading if necessary.
    /// </summary>
    public async Task<string> EnsureServerAsync(
        string? version = null,
        LlamaServerBackend? preferredBackend = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var asset = await GetAssetAsync(version, preferredBackend, cancellationToken);
        if (asset == null)
        {
            throw new InvalidOperationException(
                $"No llama-server binary found for platform {GetCurrentPlatform()}, " +
                $"architecture {GetCurrentArchitecture()}, backend {preferredBackend ?? GetPreferredBackend(GetCurrentPlatform())}");
        }

        return await DownloadAsync(asset, progress, cancellationToken);
    }

    /// <summary>
    /// Gets the path to a cached llama-server version, or null if not cached.
    /// </summary>
    public string? GetCachedServerPath(string version, LlamaServerBackend backend)
    {
        var versionDir = Path.Combine(_cacheDirectory, version, backend.ToString().ToLowerInvariant());
        var serverPath = GetServerExecutablePath(versionDir, GetCurrentPlatform());

        return File.Exists(serverPath) ? serverPath : null;
    }

    /// <summary>
    /// Gets all cached versions.
    /// </summary>
    public IReadOnlyList<string> GetCachedVersions()
    {
        if (!Directory.Exists(_cacheDirectory))
            return Array.Empty<string>();

        return Directory.GetDirectories(_cacheDirectory)
            .Select(Path.GetFileName)
            .Where(v => v != null && v.StartsWith('b'))
            .OrderByDescending(v => v)
            .ToList()!;
    }

    private static string GetServerExecutablePath(string directory, LlamaServerPlatform platform)
    {
        var executable = platform == LlamaServerPlatform.Windows ? "llama-server.exe" : "llama-server";
        return Path.Combine(directory, executable);
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destinationDir,
        LlamaServerPlatform platform,
        CancellationToken cancellationToken)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, destinationDir, overwriteFiles: true), cancellationToken);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractTarGzAsync(archivePath, destinationDir, cancellationToken);
        }

        // Set executable permission on Unix
        if (platform != LlamaServerPlatform.Windows)
        {
            var serverPath = GetServerExecutablePath(destinationDir, platform);
            if (File.Exists(serverPath) && !OperatingSystem.IsWindows())
            {
                // chmod +x
                File.SetUnixFileMode(serverPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
    }

    private static async Task ExtractTarGzAsync(string archivePath, string destinationDir, CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(archivePath);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await System.Formats.Tar.TarFile.ExtractToDirectoryAsync(gzipStream, destinationDir, overwriteFiles: true, cancellationToken);
    }

    private static LlamaServerPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return LlamaServerPlatform.Windows;
        if (OperatingSystem.IsMacOS()) return LlamaServerPlatform.MacOS;
        return LlamaServerPlatform.Linux;
    }

    private static LlamaServerArchitecture GetCurrentArchitecture()
    {
        return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => LlamaServerArchitecture.Arm64,
            _ => LlamaServerArchitecture.X64
        };
    }

    private static LlamaServerBackend GetPreferredBackend(LlamaServerPlatform platform)
    {
        // Default preferences based on platform
        return platform switch
        {
            LlamaServerPlatform.MacOS => LlamaServerBackend.Metal,
            LlamaServerPlatform.Windows => LlamaServerBackend.Vulkan, // Vulkan has good AMD/Intel/NVIDIA support
            LlamaServerPlatform.Linux => LlamaServerBackend.Vulkan,
            _ => LlamaServerBackend.Cpu
        };
    }

    private static Regex GetAssetPattern(LlamaServerPlatform platform, LlamaServerArchitecture arch, LlamaServerBackend backend)
    {
        var os = platform switch
        {
            LlamaServerPlatform.Windows => "win",
            LlamaServerPlatform.MacOS => "macos",
            LlamaServerPlatform.Linux => "ubuntu",
            _ => throw new NotSupportedException()
        };

        var archStr = arch switch
        {
            LlamaServerArchitecture.Arm64 => "arm64",
            LlamaServerArchitecture.X64 => "x64",
            _ => throw new NotSupportedException()
        };

        var backendStr = backend switch
        {
            LlamaServerBackend.Cpu => "cpu",
            LlamaServerBackend.Vulkan => "vulkan",
            LlamaServerBackend.Cuda12 => "cuda-12",
            LlamaServerBackend.Cuda13 => "cuda-13",
            LlamaServerBackend.Hip => "hip",
            LlamaServerBackend.Sycl => "sycl",
            LlamaServerBackend.Metal => "", // macOS arm64 has Metal by default
            _ => throw new NotSupportedException()
        };

        // Build pattern based on backend
        if (backend == LlamaServerBackend.Metal && platform == LlamaServerPlatform.MacOS)
        {
            // macOS arm64 Metal: llama-b7898-bin-macos-arm64.tar.gz
            return new Regex($@"llama-b\d+-bin-{os}-{archStr}\.(zip|tar\.gz)$", RegexOptions.IgnoreCase);
        }

        if (backend == LlamaServerBackend.Cpu)
        {
            // CPU build: llama-b7898-bin-win-cpu-x64.zip
            if (platform == LlamaServerPlatform.Windows)
                return new Regex($@"llama-b\d+-bin-{os}-cpu-{archStr}\.zip$", RegexOptions.IgnoreCase);
            // Linux CPU: llama-b7898-bin-ubuntu-x64.tar.gz (no "cpu" in name)
            return new Regex($@"llama-b\d+-bin-{os}-{archStr}\.(zip|tar\.gz)$", RegexOptions.IgnoreCase);
        }

        // GPU builds: llama-b7898-bin-win-vulkan-x64.zip
        // CUDA builds have minor version: llama-b7902-bin-win-cuda-12.4-x64.zip
        // HIP builds have suffix: llama-b7902-bin-win-hip-radeon-x64.zip
        var backendPattern = backend switch
        {
            LlamaServerBackend.Cuda12 or LlamaServerBackend.Cuda13 => $@"{backendStr}\.\d+",
            LlamaServerBackend.Hip => @"hip-radeon",
            _ => backendStr
        };
        return new Regex($@"llama-b\d+-bin-{os}-{backendPattern}-{archStr}\.(zip|tar\.gz)$", RegexOptions.IgnoreCase);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
