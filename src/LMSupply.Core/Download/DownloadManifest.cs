using System.Text.Json;
using System.Text.Json.Serialization;

namespace LMSupply.Download;

public sealed class DownloadManifest
{
    private const string FileName = ".lmsupply-manifest.json";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public int Version { get; set; } = 1;
    public DateTimeOffset CompletedAt { get; set; }
    public string? RepoId { get; set; }
    public string? Revision { get; set; }
    public List<ManifestFileEntry> Files { get; set; } = [];

    public static async Task WriteAsync(string directoryPath, DownloadManifest manifest)
    {
        manifest.CompletedAt = DateTimeOffset.UtcNow;
        var path = Path.Combine(directoryPath, FileName);
        var json = JsonSerializer.Serialize(manifest, s_jsonOptions);

        // Retry on transient file lock (e.g., rapid host restart releasing handles).
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await File.WriteAllTextAsync(path, json);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(100);
            }
        }
    }

    public static async Task<DownloadManifest?> ReadAsync(string directoryPath)
    {
        var path = Path.Combine(directoryPath, FileName);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<DownloadManifest>(json, s_jsonOptions);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the manifest synchronously. Returns null if not found or corrupt.
    /// </summary>
    public static DownloadManifest? Read(string directoryPath)
    {
        var path = Path.Combine(directoryPath, FileName);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DownloadManifest>(json, s_jsonOptions);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static DownloadManifest CreateFromDirectory(
        string directoryPath, string? repoId = null, string? revision = null)
    {
        var files = Directory.GetFiles(directoryPath)
            .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                     && !Path.GetFileName(f).Equals(FileName, StringComparison.OrdinalIgnoreCase))
            .Select(f => new ManifestFileEntry
            {
                Path = Path.GetFileName(f),
                Size = new FileInfo(f).Length
            })
            .ToList();

        return new DownloadManifest
        {
            RepoId = repoId,
            Revision = revision,
            Files = files
        };
    }
}

public sealed class ManifestFileEntry
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
}
