namespace LMSupply.Hardware;

/// <summary>
/// Result of evaluating whether a model fits in available VRAM,
/// with recommended GPU offload and inference settings.
/// </summary>
public sealed record VramFitResult
{
    /// <summary>Whether the model can run (always true — CPU fallback).</summary>
    public required bool Fits { get; init; }

    /// <summary>Available VRAM in bytes.</summary>
    public required long AvailableVramBytes { get; init; }

    /// <summary>Estimated model size in bytes.</summary>
    public required long ModelSizeBytes { get; init; }

    /// <summary>
    /// Recommended GPU layer count.
    /// -1 = all layers, 0 = CPU only, N = partial offload.
    /// </summary>
    public required int GpuLayerCount { get; init; }

    /// <summary>GPU offload ratio (0.0–1.0).</summary>
    public required float OffloadRatio { get; init; }

    /// <summary>Recommended batch size for prompt processing.</summary>
    public required uint RecommendedBatchSize { get; init; }

    /// <summary>Recommended KV cache quantization type.</summary>
    public required KvCacheQuantizationType RecommendedKvQuantType { get; init; }

    /// <summary>Remaining VRAM after model loading (bytes).</summary>
    public long RemainingVramBytes => Math.Max(0, AvailableVramBytes - (long)(ModelSizeBytes * OffloadRatio));

    /// <summary>
    /// Evaluates VRAM fit and produces optimal settings.
    /// </summary>
    public static VramFitResult Evaluate(long availableVramBytes, long modelSizeBytes, int totalLayers)
    {
        // No GPU: CPU-only mode
        if (availableVramBytes <= 0)
        {
            return new VramFitResult
            {
                Fits = true,
                AvailableVramBytes = 0,
                ModelSizeBytes = modelSizeBytes,
                GpuLayerCount = 0,
                OffloadRatio = 0f,
                RecommendedBatchSize = 512,
                RecommendedKvQuantType = KvCacheQuantizationType.F16
            };
        }

        float ratio;
        int gpuLayers;

        if (modelSizeBytes <= (long)(availableVramBytes * 0.7))
        {
            // Model fits comfortably (uses ≤70% VRAM): full GPU
            ratio = 1.0f;
            gpuLayers = -1;
        }
        else if (modelSizeBytes <= availableVramBytes)
        {
            // Model fits but tight: full GPU with conservative settings
            ratio = 1.0f;
            gpuLayers = -1;
        }
        else
        {
            // Model exceeds VRAM: partial offload
            ratio = Math.Clamp((float)availableVramBytes / modelSizeBytes, 0f, 1f);
            gpuLayers = Math.Max(1, (int)(totalLayers * ratio));
        }

        var remainingVram = Math.Max(0, availableVramBytes - (long)(modelSizeBytes * ratio));

        // Batch size scales with remaining VRAM
        var batchSize = remainingVram switch
        {
            >= 4L * 1024 * 1024 * 1024 => 4096u,
            >= 2L * 1024 * 1024 * 1024 => 2048u,
            >= 1L * 1024 * 1024 * 1024 => 1024u,
            _ => 512u
        };

        // KV cache quantization based on VRAM pressure
        var kvQuant = remainingVram switch
        {
            >= 2L * 1024 * 1024 * 1024 => KvCacheQuantizationType.Q8_0,
            _ => KvCacheQuantizationType.Q4_0
        };

        return new VramFitResult
        {
            Fits = true,
            AvailableVramBytes = availableVramBytes,
            ModelSizeBytes = modelSizeBytes,
            GpuLayerCount = gpuLayers,
            OffloadRatio = ratio,
            RecommendedBatchSize = batchSize,
            RecommendedKvQuantType = kvQuant
        };
    }
}
