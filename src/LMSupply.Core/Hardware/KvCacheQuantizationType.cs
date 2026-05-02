namespace LMSupply.Hardware;

/// <summary>
/// Quantization types for KV cache memory optimization.
/// </summary>
#pragma warning disable CA1707 // Identifiers should not contain underscores — standard quantization type names
public enum KvCacheQuantizationType
{
    /// <summary>
    /// Auto-select based on GPU backend and server version.
    /// CUDA/Metal/Hip → Q8_0, Vulkan b8500+ → Q8_0, CPU/SYCL → F16.
    /// </summary>
    Auto = -1,

    /// <summary>
    /// 16-bit floating point (default, highest quality).
    /// </summary>
    F16 = 0,

    /// <summary>
    /// 8-bit quantization. Good balance of memory savings and quality.
    /// Reduces KV cache memory by ~50% with minimal quality loss.
    /// </summary>
    Q8_0 = 1,

    /// <summary>
    /// 4-bit quantization. Maximum memory savings.
    /// Reduces KV cache memory by ~75% but may affect output quality.
    /// </summary>
    Q4_0 = 2,

    /// <summary>
    /// 32-bit floating point. Maximum quality, highest memory usage.
    /// </summary>
    F32 = 3
}
