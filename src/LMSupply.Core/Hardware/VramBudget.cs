using LMSupply.Runtime;

namespace LMSupply.Hardware;

/// <summary>
/// Calculates available VRAM budget for model loading.
/// Uses runtime free memory detection with fallback to total VRAM estimation.
/// </summary>
public static class VramBudget
{
    /// <summary>
    /// Default safety margin (15%) to reserve for OS, other processes, and runtime overhead.
    /// </summary>
    public const double DefaultSafetyMargin = 0.15;

    /// <summary>
    /// Gets available VRAM bytes for model loading from the current hardware.
    /// </summary>
    public static long GetAvailableBytes(double safetyMargin = DefaultSafetyMargin)
        => GetAvailableBytes(HardwareProfile.Current.GpuInfo, safetyMargin);

    /// <summary>
    /// Gets available VRAM bytes for model loading from the specified GPU.
    /// Uses FreeMemoryBytes if available (NVML), otherwise TotalMemoryBytes with safety margin.
    /// </summary>
    public static long GetAvailableBytes(GpuInfo gpu, double safetyMargin = DefaultSafetyMargin)
    {
        var rawBytes = gpu.FreeMemoryBytes ?? gpu.TotalMemoryBytes;
        if (rawBytes is null or <= 0)
            return 0;

        var usable = (long)(rawBytes.Value * (1.0 - Math.Clamp(safetyMargin, 0.0, 0.5)));
        return Math.Max(usable, 0);
    }

    /// <summary>
    /// Checks if a model of the given size can fit in available VRAM.
    /// </summary>
    public static bool CanFitModel(GpuInfo gpu, long modelSizeBytes, double safetyMargin = DefaultSafetyMargin)
        => modelSizeBytes <= GetAvailableBytes(gpu, safetyMargin);

    /// <summary>
    /// Checks if a model can fit using current hardware.
    /// </summary>
    public static bool CanFitModel(long modelSizeBytes, double safetyMargin = DefaultSafetyMargin)
        => CanFitModel(HardwareProfile.Current.GpuInfo, modelSizeBytes, safetyMargin);
}
