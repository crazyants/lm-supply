namespace LMSupply.Hardware;

/// <summary>
/// Estimates model memory footprint for VRAM budget calculations.
/// Uses known file sizes when available, falls back to parameter-based estimation.
/// </summary>
public static class ModelMemoryEstimator
{
    private const double RuntimeOverheadFactor = 1.1;

    /// <summary>
    /// Estimates the model weight size in bytes.
    /// If knownSizeBytes is provided, returns it directly.
    /// Otherwise estimates from parameter count and quantization type.
    /// </summary>
    public static long EstimateModelSizeBytes(
        long parameterCount,
        string? quantizationType = null,
        long? knownSizeBytes = null)
    {
        if (knownSizeBytes is > 0)
            return knownSizeBytes.Value;

        var bytesPerParam = GetBytesPerParameter(quantizationType);
        return (long)(parameterCount * bytesPerParam * RuntimeOverheadFactor);
    }

    /// <summary>
    /// Estimates KV cache memory usage in bytes.
    /// Formula: 2 (K+V) × numLayers × contextLength × hiddenSize × bytesPerElement
    /// </summary>
    public static long EstimateKvCacheBytes(
        int contextLength,
        int numLayers,
        int hiddenSize,
        KvCacheQuantizationType kvQuantType = KvCacheQuantizationType.F16)
    {
        var bytesPerElement = kvQuantType switch
        {
            KvCacheQuantizationType.F32 => 4.0,
            KvCacheQuantizationType.F16 => 2.0,
            KvCacheQuantizationType.Q8_0 => 1.0,
            KvCacheQuantizationType.Q4_0 => 0.5,
            _ => 2.0
        };

        return (long)(2.0 * numLayers * contextLength * hiddenSize * bytesPerElement);
    }

    /// <summary>
    /// Gets bytes per parameter for a given quantization type string.
    /// </summary>
    public static double GetBytesPerParameter(string? quantizationType)
    {
        if (string.IsNullOrWhiteSpace(quantizationType))
            return 4.0; // FP32

        var normalized = quantizationType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "q4_k_m" or "q4_k_s" or "q4_0" or "q4_1" or "int4" or "q4" or "4bit"
                => 0.5625,
            "q5_k_m" or "q5_k_s" or "q5_0" or "q5_1"
                => 0.6875,
            "q6_k"
                => 0.8125,
            "q8_0" or "q8" or "int8" or "quantized"
                => 1.0,
            "fp16" or "f16" or "half" or "float16"
                => 2.0,
            "fp32" or "f32" or "float32" or "full" or "default"
                => 4.0,
            "iq2_xxs" or "iq2_xs" => 0.3125,
            "iq3_xxs" or "iq3_xs" or "iq3_s" => 0.4375,
            "iq4_xs" or "iq4_nl" => 0.5625,
            _ => 2.0
        };
    }

    /// <summary>
    /// Estimates total VRAM needed: model weights + KV cache.
    /// </summary>
    public static long EstimateTotalVramBytes(
        long parameterCount,
        string? quantizationType,
        long? knownSizeBytes,
        int contextLength,
        int numLayers,
        int hiddenSize,
        KvCacheQuantizationType kvQuantType = KvCacheQuantizationType.F16)
    {
        var modelSize = EstimateModelSizeBytes(parameterCount, quantizationType, knownSizeBytes);
        var kvCache = EstimateKvCacheBytes(contextLength, numLayers, hiddenSize, kvQuantType);
        return modelSize + kvCache;
    }
}
