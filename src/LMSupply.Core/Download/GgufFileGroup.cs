using System.Text.RegularExpressions;

namespace LMSupply.Core.Download;

/// <summary>
/// Raw GGUF file info from HuggingFace API (filename + size).
/// </summary>
public record GgufRawFile(string FileName, long SizeBytes);

/// <summary>
/// A logical GGUF model group — may consist of one file or multiple split parts.
/// Handles the -00001-of-00003 split file pattern common for large models.
/// </summary>
public sealed record GgufFileGroup
{
    // Pattern: anything ending in -NNNNN-of-NNNNN.gguf (5 digits each side)
    private static readonly Regex SplitPattern =
        new(@"-(\d{5})-of-(\d{5})\.gguf$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>First part filename (or the only filename for non-split).</summary>
    public required string PrimaryFileName { get; init; }

    /// <summary>All part filenames in sorted order.</summary>
    public required IReadOnlyList<string> Parts { get; init; }

    /// <summary>Sum of all part sizes in bytes.</summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>True if this group consists of multiple split parts.</summary>
    public bool IsSplit => Parts.Count > 1;

    /// <summary>Total size in gigabytes.</summary>
    public double TotalSizeGB => TotalSizeBytes / (1024.0 * 1024 * 1024);

    /// <summary>
    /// Detects if a filename is a split-part file (e.g. model-Q4_K_M-00001-of-00003.gguf).
    /// </summary>
    public static bool IsSplitPart(string filename) =>
        SplitPattern.IsMatch(filename);

    /// <summary>
    /// Returns the base name without the split suffix and without the .gguf extension.
    /// e.g. "model-Q4_K_M-00001-of-00003.gguf" → "model-Q4_K_M"
    ///      "model-Q4_K_M.gguf"                → "model-Q4_K_M"
    /// </summary>
    public static string GetBaseName(string filename)
    {
        var match = SplitPattern.Match(filename);
        if (match.Success)
            return filename[..match.Index];

        return Path.GetFileNameWithoutExtension(filename);
    }

    /// <summary>
    /// Groups raw GGUF files into logical model groups.
    /// Split parts (e.g. -00001-of-00003) are merged into one group with summed sizes.
    /// </summary>
    public static IEnumerable<GgufFileGroup> GroupFiles(IEnumerable<GgufRawFile> files)
    {
        var grouped = new Dictionary<string, List<GgufRawFile>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var baseName = GetBaseName(file.FileName);
            if (!grouped.TryGetValue(baseName, out var list))
            {
                list = [];
                grouped[baseName] = list;
            }
            list.Add(file);
        }

        foreach (var (_, parts) in grouped)
        {
            var sorted = parts
                .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            yield return new GgufFileGroup
            {
                PrimaryFileName = sorted[0].FileName,
                Parts = sorted.Select(f => f.FileName).ToList(),
                TotalSizeBytes = sorted.Sum(f => f.SizeBytes)
            };
        }
    }
}
