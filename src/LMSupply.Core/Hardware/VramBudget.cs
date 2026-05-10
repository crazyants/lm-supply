using System.Globalization;
using System.Runtime.InteropServices;
using LMSupply.Runtime;

namespace LMSupply.Hardware;

/// <summary>
/// Calculates available VRAM budget for model loading.
/// Budget is <c>min(total × (1 - margin), free × <see cref="FreeVramSafetyFactor"/>)</c>:
/// the total-based cap guards thermal/stability headroom; the free-based cap prevents
/// selecting a model that exceeds what is actually available when other processes share the GPU.
/// Absolute override via environment variable <see cref="BudgetOverrideEnvVar"/> (megabytes).
/// </summary>
public static class VramBudget
{
    /// <summary>
    /// Environment variable that overrides the computed budget with an absolute value in megabytes.
    /// When set to a positive integer, the value is returned as-is (safety margin is not applied).
    /// Use this to force a specific budget when the auto-calculated value is incorrect
    /// (e.g., <c>LMSUPPLY_VRAM_BUDGET_MB=10000</c> for a 10 GB budget).
    /// </summary>
    public const string BudgetOverrideEnvVar = "LMSUPPLY_VRAM_BUDGET_MB";

    /// <summary>
    /// Safety factor applied to the free VRAM reading when computing the budget ceiling.
    /// Free VRAM is probed at startup and can fluctuate between probe and model load;
    /// retaining a 5 % buffer prevents selecting a model that barely fits the polled reading.
    /// </summary>
    public const double FreeVramSafetyFactor = 0.95;

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
    /// Returns <c>min(total × (1 - margin), free × <see cref="FreeVramSafetyFactor"/>)</c>.
    /// Prefers TotalMemoryBytes; falls back to FreeMemoryBytes only when total is unknown.
    /// </summary>
    public static long GetAvailableBytes(GpuInfo gpu)
        => GetAvailableBytes(gpu, GetRecommendedSafetyMargin(gpu));

    /// <summary>
    /// Gets available VRAM bytes for model loading from the specified GPU using an explicit safety margin.
    /// Returns <c>min(total × (1 - margin), free × <see cref="FreeVramSafetyFactor"/>)</c> so that
    /// other processes sharing the GPU do not cause an over-large budget.
    /// When only one of total/free is known, uses whichever is available.
    /// Honors <see cref="BudgetOverrideEnvVar"/> as an absolute override (MB, margin ignored).
    /// </summary>
    public static long GetAvailableBytes(GpuInfo gpu, double safetyMargin)
    {
        if (TryGetEnvOverrideBytes(out var overrideBytes))
            return overrideBytes;

        var clampedMargin = Math.Clamp(safetyMargin, 0.0, 0.5);

        long? totalCap = gpu.TotalMemoryBytes is > 0
            ? (long)(gpu.TotalMemoryBytes.Value * (1.0 - clampedMargin))
            : null;

        long? freeCap = gpu.FreeMemoryBytes is > 0
            ? (long)(gpu.FreeMemoryBytes.Value * FreeVramSafetyFactor)
            : null;

        var usable = (totalCap, freeCap) switch
        {
            ({ } t, { } f) => Math.Min(t, f),
            ({ } t, null)  => t,
            (null, { } f)  => f,
            _              => 0L,
        };

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
