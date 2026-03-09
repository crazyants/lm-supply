using System.Diagnostics;
using LMSupply.Hardware;

namespace LMSupply.Synthesizer.Models;

/// <summary>
/// Model registry for the Synthesizer domain.
/// </summary>
public sealed class SynthesizerModelRegistry : ModelRegistryBase<SynthesizerModelInfo>
{
    /// <summary>
    /// Auto-selection candidates sorted by size descending (largest first).
    /// TTS models are small (~16-64MB), so VRAM is rarely a constraint;
    /// this ordering naturally selects highest quality when possible.
    /// </summary>
    private static readonly SynthesizerModelInfo[] AutoCandidates =
    [
        DefaultModels.EnUsAmy,     // 64MB - quality
        DefaultModels.EnUsLessac,  // 63MB - default
        DefaultModels.EnUsRyan,    // 16MB - fast
    ];

    /// <summary>
    /// Gets the default registry instance with built-in models.
    /// </summary>
    public static SynthesizerModelRegistry Default { get; } = new(DefaultModels.All);

    /// <summary>
    /// Initializes a new registry with the specified system models.
    /// </summary>
    /// <param name="systemModels">Models to register as system defaults.</param>
    public SynthesizerModelRegistry(IEnumerable<SynthesizerModelInfo> systemModels)
        : base(systemModels) { }

    /// <summary>
    /// Gets the optimal model based on available VRAM.
    /// Selects the largest model that fits in available GPU memory,
    /// falling back to the smallest model if none fit.
    /// </summary>
    protected override SynthesizerModelInfo GetAutoModel()
    {
        var gpu = HardwareProfile.Current.GpuInfo;
        var availableVram = VramBudget.GetAvailableBytes(gpu);
        Trace.TraceInformation($"[SynthesizerModelRegistry] Auto-selecting model for VRAM: {availableVram / (1024 * 1024)} MB");

        foreach (var candidate in AutoCandidates)
        {
            var memInfo = (IModelMemoryInfo)candidate;
            var size = ModelMemoryEstimator.EstimateModelSizeBytes(
                memInfo.ParameterCount, memInfo.QuantizationType, memInfo.EstimatedSizeBytes);
            if (size <= availableVram)
            {
                Trace.TraceInformation($"[SynthesizerModelRegistry] Selected: {candidate.Id} ({size / (1024 * 1024)} MB)");
                return candidate with { AliasName = "auto" };
            }
        }

        // Fallback to smallest model
        var fallback = AutoCandidates[^1];
        Trace.TraceInformation($"[SynthesizerModelRegistry] Fallback to smallest: {fallback.Id}");
        return fallback with { AliasName = "auto" };
    }

    /// <summary>
    /// Creates a fallback model info for unknown model IDs (HuggingFace repos or local paths).
    /// </summary>
    protected override SynthesizerModelInfo CreateFallbackModelInfo(string modelId)
    {
        Trace.TraceInformation($"[SynthesizerModelRegistry] Creating fallback model info for: {modelId}");

        var parts = modelId.Split('/');
        var name = parts.Length > 1 ? parts[1] : modelId;

        return new SynthesizerModelInfo
        {
            Id = modelId,
            AliasName = modelId,
            DisplayName = name,
            Architecture = "VITS"
        };
    }
}
