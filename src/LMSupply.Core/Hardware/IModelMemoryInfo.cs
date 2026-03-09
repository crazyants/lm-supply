namespace LMSupply.Hardware;

/// <summary>
/// Provides memory footprint information for VRAM budget calculations.
/// Domain ModelInfo types implement this to participate in VRAM-aware model selection.
/// </summary>
public interface IModelMemoryInfo
{
    /// <summary>
    /// Known model file size in bytes. Null if unknown (use parameter-based estimation).
    /// </summary>
    long? EstimatedSizeBytes { get; }

    /// <summary>
    /// Total parameter count for estimation fallback.
    /// </summary>
    long ParameterCount { get; }

    /// <summary>
    /// Quantization type string for estimation (e.g., "Q4_K_M", "fp16", "int8").
    /// Null means full precision (FP32).
    /// </summary>
    string? QuantizationType { get; }
}
