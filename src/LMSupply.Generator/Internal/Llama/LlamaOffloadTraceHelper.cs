using System.Diagnostics;

namespace LMSupply.Generator.Internal.Llama;

/// <summary>
/// Emits Trace events describing GPU offload decisions when a model partially or fully falls back to CPU.
/// Extracted from LlamaServerGeneratorModel for testability and severity-aware logging.
/// </summary>
/// <remarks>
/// Severity rationale:
/// - 0 GPU layers (full CPU fallback): Warning. Inference is typically 60-100x slower than partial GPU offload,
///   yet startup completes silently with no visible signal to the operator. Operators reading host logs need to
///   distinguish "model loaded successfully on GPU" from "model loaded but is going to be unusably slow."
/// - Partial offload (1..N-1 layers on GPU): Information. Some performance degradation but model is functional.
/// </remarks>
internal static class LlamaOffloadTraceHelper
{
    private const double BytesPerMb = 1024.0 * 1024.0;
    private const double BytesPerGb = BytesPerMb * 1024.0;

    /// <summary>
    /// Emits a severity-aware Trace event describing the offload outcome.
    /// </summary>
    /// <param name="estimate">The resource estimate that decided the GPU layer count.</param>
    /// <param name="freeVramBytes">Current free VRAM in bytes (HardwareProfile snapshot at decision time).</param>
    /// <param name="totalVramBytes">Total VRAM in bytes.</param>
    public static void TraceOffloadDecision(
        ResourceEstimate estimate,
        long freeVramBytes,
        long totalVramBytes)
    {
        if (estimate.RecommendedGpuLayers == 0)
        {
            Trace.TraceWarning(
                $"[LlamaServerGeneratorModel] CPU-only fallback: 0/{estimate.TotalLayers} layers on GPU. " +
                $"Free VRAM {freeVramBytes / BytesPerMb:F0}MB / Total {totalVramBytes / BytesPerMb:F0}MB " +
                $"is insufficient for any GPU offload. " +
                $"Model will run entirely on RAM ({estimate.EstimatedRamBytes / BytesPerGb:F1}GB). " +
                $"Performance severely degraded. " +
                $"To override: set LMSUPPLY_VRAM_BUDGET_MB env var or close other GPU-using processes.");
        }
        else
        {
            Trace.TraceInformation(
                $"[LlamaServerGeneratorModel] Auto partial offload: " +
                $"{estimate.RecommendedGpuLayers}/{estimate.TotalLayers} layers on GPU " +
                $"(VRAM: {estimate.EstimatedVramBytes / BytesPerGb:F1}GB, " +
                $"RAM: {estimate.EstimatedRamBytes / BytesPerGb:F1}GB)");
        }
    }
}
