namespace LMSupply.Generator.Internal.Llama;

/// <summary>
/// Why a particular GGUF model was chosen by auto-selection.
/// </summary>
public enum ModelSelectionReason
{
    /// <summary>The selected model fits within the VRAM budget (weights + KV cache).</summary>
    Fits,

    /// <summary>
    /// No registered model fits within the budget. The smallest available model
    /// was returned as a best-effort fallback; runtime may still OOM or partial-offload.
    /// </summary>
    FallbackToSmallest,
}

/// <summary>
/// One candidate evaluated during auto-selection.
/// </summary>
public sealed record ModelSelectionCandidate
{
    /// <summary>The candidate model.</summary>
    public required GgufModelInfo Model { get; init; }

    /// <summary>Estimated weight memory in bytes (registry value or computed from params).</summary>
    public required long WeightsBytes { get; init; }

    /// <summary>Estimated KV cache size in bytes at the budget context length.</summary>
    public required long KvCacheBytes { get; init; }

    /// <summary>Total estimated VRAM footprint (weights + KV cache).</summary>
    public long TotalBytes => WeightsBytes + KvCacheBytes;

    /// <summary>Whether this candidate fits within the VRAM budget.</summary>
    public required bool Fits { get; init; }
}

/// <summary>
/// Result of auto-selecting a GGUF model, including the chosen model,
/// budget information, and the candidates that were evaluated.
/// Intended for diagnostic logging.
/// </summary>
public sealed record ModelSelectionResult
{
    /// <summary>The model that was selected.</summary>
    public required GgufModelInfo Selected { get; init; }

    /// <summary>Why this model was selected (fits or fallback).</summary>
    public required ModelSelectionReason Reason { get; init; }

    /// <summary>Available VRAM in bytes used as the fit budget.</summary>
    public required long AvailableVramBytes { get; init; }

    /// <summary>Safety margin applied to raw free VRAM to compute the budget.</summary>
    public required double SafetyMargin { get; init; }

    /// <summary>Context length used for KV cache estimation.</summary>
    public required int BudgetContextLength { get; init; }

    /// <summary>All candidates evaluated during selection (sorted descending by total size).</summary>
    public required IReadOnlyList<ModelSelectionCandidate> Candidates { get; init; }
}
