namespace LMSupply.Generator.Abstractions;

/// <summary>
/// Diagnostics describing how the auto-selection logic chose the loaded model.
/// Populated when <c>LocalGenerator.LoadAsync</c> runs through the auto path; null otherwise.
/// </summary>
public sealed record SelectionDiagnostics
{
    /// <summary>Total VRAM reported by the GPU probe, in bytes. Null when unavailable.</summary>
    public long? TotalVramBytes { get; init; }

    /// <summary>Free VRAM at probe time, in bytes. Null when unavailable (e.g., no NVML).</summary>
    public long? FreeVramBytes { get; init; }

    /// <summary>The budget (in bytes) used as the fit threshold for candidates.</summary>
    public required long BudgetVramBytes { get; init; }

    /// <summary>Safety margin applied to derive the budget (0.0–1.0). Zero when overridden by env var.</summary>
    public required double SafetyMargin { get; init; }

    /// <summary>Reason string for the selection (e.g., "Fits", "FallbackToSmallest"). Null for non-GGUF paths.</summary>
    public string? SelectionReason { get; init; }

    /// <summary>True when the budget came from the <c>LMSUPPLY_VRAM_BUDGET_MB</c> environment override.</summary>
    public bool EnvOverrideApplied { get; init; }
}
