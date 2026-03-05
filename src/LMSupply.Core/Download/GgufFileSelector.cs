namespace LMSupply.Core.Download;

/// <summary>
/// Represents available hardware memory for GGUF model selection.
/// </summary>
/// <param name="VramBytes">GPU VRAM in bytes (0 for CPU-only systems).</param>
/// <param name="RamBytes">System RAM in bytes.</param>
public record AvailableMemory(long VramBytes, long RamBytes)
{
    // Reserve 2GB for GPU driver and system overhead
    private const long GpuOverheadBytes = 2L * 1024 * 1024 * 1024;
    // Reserve 4GB for OS and system processes
    private const long CpuOverheadBytes = 4L * 1024 * 1024 * 1024;
    // Add 10% runtime overhead for model loading structures
    private const double RuntimeOverheadFactor = 1.10;

    /// <summary>Usable GPU VRAM after deducting system overhead.</summary>
    public long UsableVramBytes => Math.Max(0, VramBytes - GpuOverheadBytes);

    /// <summary>Usable system RAM after deducting OS overhead.</summary>
    public long UsableRamBytes => Math.Max(0, RamBytes - CpuOverheadBytes);

    /// <summary>Total usable memory (VRAM + RAM) for hybrid GPU/CPU inference.</summary>
    public long TotalUsableBytes => UsableVramBytes + UsableRamBytes;

    /// <summary>
    /// Returns true if the file fits entirely in GPU VRAM (enables full GPU offload = fastest).
    /// </summary>
    public bool FitsInGpu(long fileSizeBytes) =>
        VramBytes > 0 && (long)(fileSizeBytes * RuntimeOverheadFactor) <= UsableVramBytes;

    /// <summary>
    /// Returns true if the file fits in total available memory (GPU + RAM).
    /// Covers CPU-only, full-GPU, and partial GPU offload scenarios.
    /// </summary>
    public bool FitsInMemory(long fileSizeBytes) =>
        (long)(fileSizeBytes * RuntimeOverheadFactor) <= TotalUsableBytes;
}

/// <summary>
/// Selects the optimal GGUF file group based on hardware memory constraints.
///
/// Algorithm:
/// 1. Filter groups that fit in available memory (size × 1.1 ≤ total usable memory)
/// 2. If preferred quantization specified and fits, select it
/// 3. Otherwise, select the largest fitting group (larger file = higher quality for same architecture)
/// 4. If nothing fits, throw a descriptive exception
/// </summary>
public static class GgufFileSelector
{
    /// <summary>
    /// Selects the best GGUF file group for the given hardware memory constraints.
    /// </summary>
    /// <param name="groups">Available file groups (e.g. from GgufFileGroup.GroupFiles).</param>
    /// <param name="memory">Available hardware memory specification.</param>
    /// <param name="preferredQuantization">
    /// Optional user-specified quantization type (e.g. "Q8_0", "Q6_K").
    /// If the preferred type fits in memory, it is selected. Otherwise falls back to auto.
    /// </param>
    /// <returns>The best fitting file group.</returns>
    /// <exception cref="ArgumentException">Thrown when groups is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no file fits in available memory, with details about required vs available memory.
    /// </exception>
    public static GgufFileGroup Select(
        IEnumerable<GgufFileGroup> groups,
        AvailableMemory memory,
        string? preferredQuantization = null)
    {
        var list = groups.ToList();

        if (list.Count == 0)
            throw new ArgumentException("No GGUF file groups provided.", nameof(groups));

        // Filter to groups that fit in available memory, sorted by size descending (best quality first)
        var fitting = list
            .Where(g => memory.FitsInMemory(g.TotalSizeBytes))
            .OrderByDescending(g => g.TotalSizeBytes)
            .ToList();

        if (fitting.Count == 0)
        {
            var smallest = list.MinBy(g => g.TotalSizeBytes)!;
            var availableGB = memory.TotalUsableBytes / (1024.0 * 1024 * 1024);
            throw new InvalidOperationException(
                $"No GGUF file fits in available memory. " +
                $"Smallest option is {smallest.TotalSizeGB:F1}GB ({smallest.PrimaryFileName}), " +
                $"but only {availableGB:F1}GB is available. " +
                "Consider using a smaller model or freeing system memory.");
        }

        // If user specified a preferred quantization and it fits, use it
        if (!string.IsNullOrWhiteSpace(preferredQuantization))
        {
            var preferred = fitting.FirstOrDefault(g =>
                MatchesQuantization(g.PrimaryFileName, preferredQuantization));

            if (preferred is not null)
                return preferred;
            // Falls through to auto selection if preferred doesn't fit or not found
        }

        // Auto: return the largest fitting group (= highest quality within memory budget)
        return fitting[0];
    }

    /// <summary>
    /// Checks if a filename contains the given quantization type.
    /// Uses case-insensitive substring matching to handle various naming conventions:
    /// - Standard: model-Q4_K_M.gguf
    /// - iMatrix: model-Q4_K_M-imat.gguf
    /// - Dot separator: Meta-Llama-3-8B.Q4_K_M.gguf
    /// - Underscore separator: model_Q4_K_M.gguf
    /// </summary>
    public static bool MatchesQuantization(string filename, string quantization) =>
        filename.Contains(quantization, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an AvailableMemory instance from a HardwareProfile.
    /// </summary>
    public static AvailableMemory FromHardwareProfile(Hardware.HardwareProfile profile) =>
        new(
            VramBytes: profile.GpuInfo.TotalMemoryBytes ?? 0,
            RamBytes: profile.SystemMemoryBytes);
}
