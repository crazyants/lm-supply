using System.Diagnostics;
using LMSupply.Hardware;

namespace LMSupply.Embedder.Utils;

/// <summary>
/// Model registry for the Embedder domain.
/// </summary>
public sealed class EmbedderModelRegistry : ModelRegistryBase<ModelInfo>
{
    /// <summary>
    /// Gets the default registry instance with built-in models.
    /// </summary>
    public static EmbedderModelRegistry Default { get; } = new(DefaultModels.All);

    /// <summary>
    /// Initializes a new registry with the specified system models.
    /// </summary>
    /// <param name="systemModels">Models to register as system defaults.</param>
    public EmbedderModelRegistry(IEnumerable<ModelInfo> systemModels)
        : base(systemModels) { }

    /// <summary>
    /// Gets the optimal model based on current hardware profile.
    /// Uses PerformanceTier to select appropriate model size.
    /// </summary>
    /// <remarks>
    /// Tier mapping:
    /// - Low:    bge-small-en-v1.5 (33M params, 384 dims) - lightweight
    /// - Medium: bge-base-en-v1.5 (110M params, 768 dims) - balanced
    /// - High:   gte-large-en-v1.5 (434M params, 1024 dims, 8K context) - quality
    /// - Ultra:  gte-large-en-v1.5 (434M params, 1024 dims, 8K context) - quality
    /// </remarks>
    protected override ModelInfo GetAutoModel()
    {
        var tier = HardwareProfile.Current.Tier;
        Trace.TraceInformation($"[EmbedderModelRegistry] Auto-selecting model for tier: {tier}");

        var model = tier switch
        {
            PerformanceTier.Ultra or PerformanceTier.High => DefaultModels.GteLargeEnV15,
            PerformanceTier.Medium => DefaultModels.BgeBaseEnV15,
            _ => DefaultModels.BgeSmallEnV15
        };

        return model with { AliasName = "auto" };
    }

    /// <summary>
    /// Creates a fallback model info for unknown model IDs (HuggingFace repos or local paths).
    /// </summary>
    protected override ModelInfo CreateFallbackModelInfo(string modelId)
    {
        Trace.TraceInformation($"[EmbedderModelRegistry] Creating fallback model info for: {modelId}");

        return new ModelInfo
        {
            RepoId = modelId,
            AliasName = modelId,
            Dimensions = 384,
            MaxSequenceLength = 512,
            PoolingMode = PoolingMode.Mean,
            DoLowerCase = false,
            Description = $"Custom model: {modelId}",
            Subfolder = "onnx"
        };
    }
}
