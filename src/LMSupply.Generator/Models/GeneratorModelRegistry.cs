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
    /// Also registers "fast" as a system alias for the default model (Phi4Mini),
    /// since it is the smallest FC-capable ONNX model.
    /// </summary>
    /// <param name="systemModels">Models to register as system defaults.</param>
    public GeneratorModelRegistry(IEnumerable<ModelInfo> systemModels)
        : base(AppendFastAlias(systemModels)) { }

    private static IEnumerable<ModelInfo> AppendFastAlias(IEnumerable<ModelInfo> models)
    {
        foreach (var model in models)
            yield return model;

        // Register "fast" as an alias pointing to Phi4Mini
        yield return DefaultGeneratorModels.Phi4Mini with { AliasName = "fast" };
    }

    /// <summary>
    /// Gets the optimal ONNX model based on current hardware profile.
    /// Uses PerformanceTier to select appropriate model size.
    /// All models are MIT-licensed Phi-4 series with function calling support.
    /// </summary>
    /// <remarks>
    /// Tier mapping:
    /// - Low/Medium: Phi-3.5-mini (3.8B params) - legacy, lightweight
    /// - High:       Phi-4-mini (3.8B, 16K context) - default, good quality
    /// - Ultra:      Phi-4 (14B params) - highest quality
    /// </remarks>
    protected override ModelInfo GetAutoModel()
    {
        var tier = HardwareProfile.Current.Tier;
        Trace.TraceInformation($"[GeneratorModelRegistry] Auto-selecting model for tier: {tier}");

        var model = tier switch
        {
            PerformanceTier.Ultra => DefaultGeneratorModels.Phi4,
            PerformanceTier.High => DefaultGeneratorModels.Phi4Mini,
            _ => DefaultGeneratorModels.Phi35Mini
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
