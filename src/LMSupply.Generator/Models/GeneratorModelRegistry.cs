using System.Diagnostics;
using LMSupply.Hardware;

namespace LMSupply.Generator;

/// <summary>
/// Model registry for the Generator domain.
/// </summary>
public sealed class GeneratorModelRegistry : ModelRegistryBase<ModelInfo>
{
    /// <summary>
    /// Gets the default registry instance with built-in models.
    /// </summary>
    public static GeneratorModelRegistry Default { get; } = new(DefaultGeneratorModels.All);

    /// <summary>
    /// Initializes a new registry with the specified system models.
    /// </summary>
    /// <param name="systemModels">Models to register as system defaults.</param>
    public GeneratorModelRegistry(IEnumerable<ModelInfo> systemModels)
        : base(systemModels) { }

    /// <summary>
    /// Gets the optimal model based on current hardware profile.
    /// Uses PerformanceTier to select appropriate model size.
    /// Prefers MIT-licensed models when possible.
    /// </summary>
    /// <remarks>
    /// Tier mapping:
    /// - Low:    Llama-3.2-1B (1B params) - fast, lightweight
    /// - Medium: Phi-3.5-mini (3.8B params) - balanced
    /// - High:   Phi-4-mini (3.8B, 16K context) - MIT, good quality
    /// - Ultra:  Phi-4 (14B params) - MIT, highest quality
    /// </remarks>
    protected override ModelInfo GetAutoModel()
    {
        var tier = HardwareProfile.Current.Tier;
        Trace.TraceInformation($"[GeneratorModelRegistry] Auto-selecting model for tier: {tier}");

        var model = tier switch
        {
            PerformanceTier.Ultra => DefaultGeneratorModels.Phi4,
            PerformanceTier.High => DefaultGeneratorModels.Phi4Mini,
            PerformanceTier.Medium => DefaultGeneratorModels.Phi35Mini,
            _ => DefaultGeneratorModels.Llama321B
        };

        return model with { AliasName = "auto" };
    }

    /// <summary>
    /// Creates a fallback model info for unknown model IDs (HuggingFace repos or local paths).
    /// </summary>
    protected override ModelInfo CreateFallbackModelInfo(string modelId)
    {
        Trace.TraceInformation($"[GeneratorModelRegistry] Creating fallback model info for: {modelId}");

        var parts = modelId.Split('/');
        var name = parts.Length > 1 ? parts[1] : modelId;

        return new ModelInfo
        {
            ModelId = modelId,
            AliasName = modelId,
            DisplayName = name,
            Description = $"Custom model: {modelId}",
            ParameterCount = 0,
            License = LicenseTier.Conditional,
            LicenseName = "Unknown",
            ChatFormat = "chatml", // Safe default
            DefaultQuantization = Quantization.Quant4,
            RecommendedContextLength = 4096,
            NumLayers = 32,
            HiddenSize = 3072
        };
    }
}
