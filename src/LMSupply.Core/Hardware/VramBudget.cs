using System.Globalization;
using System.Runtime.InteropServices;
using LMSupply.Runtime;

namespace LMSupply.Hardware;

/// <summary>
/// Calculates available VRAM budget for model loading.
/// Uses total VRAM (the model host is assumed to own the GPU for its lifetime);
/// falls back to free VRAM only when total is unknown.
/// Absolute override via environment variable <see cref="BudgetOverrideEnvVar"/> (megabytes).
/// </summary>
public static class VramBudget
{
    /// <summary>
    /// Environment variable that overrides the computed budget with an absolute value in megabytes.
    /// When set to a positive integer, the value is returned as-is (safety margin is not applied).
    /// </summary>
    public const string BudgetOverrideEnvVar = "LMSUPPLY_VRAM_BUDGET_MB";

    /// <summary>
    /// Default safety margin (15%) to reserve for OS, other processes, and runtime overhead.
    /// </summary>
    public const double DefaultSafetyMargin = 0.15;

    /// <summary>
    /// Elevated safety margin (25%) for low-VRAM NVIDIA GPUs on Windows.
    /// Windows compositor + driver overhead are proportionally larger on small dedicated cards
    /// (RTX 4060 Laptop 4GB, etc.) and the default 15% leaves no room for KV cache growth.
    /// </summary>
    public const double LowVramWindowsSafetyMargin = 0.25;

    /// <summary>
    /// VRAM threshold below which the elevated Windows margin applies.
    /// </summary>
    public const long LowVramThresholdBytes = 6L * 1024 * 1024 * 1024;

    /// <summary>
    /// Returns the recommended safety margin for the given GPU based on platform and capacity.
    /// Windows + NVIDIA + total VRAM ≤ 6GB → 0.25 (compositor + driver overhead is large relative to VRAM).
    /// All other cases → <see cref="DefaultSafetyMargin"/>.
    /// </summary>
    public static double GetRecommendedSafetyMargin(GpuInfo gpu)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && gpu.Vendor == GpuVendor.Nvidia
            && gpu.TotalMemoryBytes is > 0 and <= LowVramThresholdBytes)
        {
            return LowVramWindowsSafetyMargin;
        }

        return DefaultSafetyMargin;
    }

    /// <summary>
    /// Gets available VRAM bytes for model loading from the current hardware,
    /// using the platform-recommended safety margin.
    /// </summary>
    public static long GetAvailableBytes()
        => GetAvailableBytes(HardwareProfile.Current.GpuInfo);

    /// <summary>
    /// Gets available VRAM bytes for model loading from the specified GPU,
    /// using the platform-recommended safety margin (see <see cref="GetRecommendedSafetyMargin"/>).
    /// Prefers TotalMemoryBytes; falls back to FreeMemoryBytes only when total is unknown.
    /// </summary>
    public static long GetAvailableBytes(GpuInfo gpu)
        => GetAvailableBytes(gpu, GetRecommendedSafetyMargin(gpu));

    /// <summary>
    /// Gets available VRAM bytes for model loading from the specified GPU using an explicit safety margin.
    /// Prefers TotalMemoryBytes (long-running hosts own the GPU for their lifetime);
    /// falls back to FreeMemoryBytes only when total is unknown.
    /// Honors <see cref="BudgetOverrideEnvVar"/> as an absolute override (MB, margin ignored).
    /// </summary>
    public static long GetAvailableBytes(GpuInfo gpu, double safetyMargin)
    {
        if (TryGetEnvOverrideBytes(out var overrideBytes))
            return overrideBytes;

        var rawBytes = gpu.TotalMemoryBytes ?? gpu.FreeMemoryBytes;
        if (rawBytes is null or <= 0)
            return 0;

        var usable = (long)(rawBytes.Value * (1.0 - Math.Clamp(safetyMargin, 0.0, 0.5)));
        return Math.Max(usable, 0);
    }

    /// <summary>
    /// Returns true if <see cref="BudgetOverrideEnvVar"/> is set to a positive integer,
    /// with the override value (bytes) in <paramref name="bytes"/>.
    /// </summary>
    public static bool TryGetEnvOverrideBytes(out long bytes)
    {
        bytes = 0;
        var raw = Environment.GetEnvironmentVariable(BudgetOverrideEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (!long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mb) || mb <= 0)
            return false;

        bytes = mb * 1024L * 1024L;
        return true;
    }

    /// <summary>
    /// Checks if a model of the given size can fit in available VRAM.
    /// </summary>
    public static bool CanFitModel(GpuInfo gpu, long modelSizeBytes, double? safetyMargin = null)
        => modelSizeBytes <= GetAvailableBytes(gpu, safetyMargin ?? GetRecommendedSafetyMargin(gpu));

    /// <summary>
    /// Checks if a model can fit using current hardware.
    /// </summary>
    public static bool CanFitModel(long modelSizeBytes, double? safetyMargin = null)
        => CanFitModel(HardwareProfile.Current.GpuInfo, modelSizeBytes, safetyMargin);
}
