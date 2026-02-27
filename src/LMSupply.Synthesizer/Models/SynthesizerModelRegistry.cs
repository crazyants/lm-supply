using System.Diagnostics;
using LMSupply.Hardware;

namespace LMSupply.Synthesizer.Models;

/// <summary>
/// Model registry for the Synthesizer domain.
/// </summary>
public sealed class SynthesizerModelRegistry : ModelRegistryBase<SynthesizerModelInfo>
{
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
    /// Gets the optimal model based on current hardware profile.
    /// Uses PerformanceTier to select appropriate model quality.
    /// </summary>
    /// <remarks>
    /// For TTS, all Piper models are similar in size (~64MB), so we select based on quality:
    /// - Low:    Ryan (fast, low quality) - 16MB
    /// - Medium: Lessac (default, medium quality) - 64MB
    /// - High:   Amy (quality, high quality) - 64MB
    /// - Ultra:  Amy (quality, high quality) - 64MB
    /// </remarks>
    protected override SynthesizerModelInfo GetAutoModel()
    {
        var tier = HardwareProfile.Current.Tier;
        Trace.TraceInformation($"[SynthesizerModelRegistry] Auto-selecting model for tier: {tier}");

        var model = tier switch
        {
            PerformanceTier.Ultra or PerformanceTier.High => DefaultModels.EnUsAmy,
            PerformanceTier.Medium => DefaultModels.EnUsLessac,
            _ => DefaultModels.EnUsRyan
        };

        return model with { AliasName = "auto" };
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
