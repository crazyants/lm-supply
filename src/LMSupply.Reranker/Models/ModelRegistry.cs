using System.Diagnostics;
using LMSupply.Hardware;

namespace LMSupply.Reranker.Models;

/// <summary>
/// Model registry for the Reranker domain.
/// </summary>
public sealed class RerankerModelRegistry : ModelRegistryBase<ModelInfo>
{
    /// <summary>
    /// Gets the default registry instance with built-in models.
    /// </summary>
    public static RerankerModelRegistry Default { get; } = new(DefaultModels.All);

    /// <summary>
    /// Initializes a new registry with the specified system models.
    /// </summary>
    /// <param name="systemModels">Models to register as system defaults.</param>
    public RerankerModelRegistry(IEnumerable<ModelInfo> systemModels)
        : base(systemModels) { }

    /// <summary>
    /// Gets the optimal model based on current hardware profile.
    /// Uses PerformanceTier to select appropriate model size.
    /// </summary>
    /// <remarks>
    /// Tier mapping:
    /// - Low:    ms-marco-MiniLM-L-6-v2 (22M params) - fast, lightweight
    /// - Medium: bge-reranker-base (278M params) - balanced, multilingual
    /// - High:   bge-reranker-large (560M params) - highest accuracy
    /// - Ultra:  bge-reranker-large (560M params) - highest accuracy
    /// </remarks>
    protected override ModelInfo GetAutoModel()
    {
        var tier = HardwareProfile.Current.Tier;
        Trace.TraceInformation($"[RerankerModelRegistry] Auto-selecting model for tier: {tier}");

        var model = tier switch
        {
            PerformanceTier.Ultra or PerformanceTier.High => DefaultModels.BgeRerankerLarge,
            PerformanceTier.Medium => DefaultModels.BgeRerankerBase,
            _ => DefaultModels.MsMarcoMiniLML6V2
        };

        return model with { AliasName = "auto" };
    }

    /// <summary>
    /// Creates a fallback model info for unknown model IDs (HuggingFace repos or local paths).
    /// </summary>
    protected override ModelInfo CreateFallbackModelInfo(string modelId)
    {
        Trace.TraceInformation($"[RerankerModelRegistry] Creating fallback model info for: {modelId}");

        // Check if it's a local path with an ONNX file
        if (modelId.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(modelId) ||
            modelId.StartsWith("./", StringComparison.Ordinal) ||
            modelId.StartsWith("../", StringComparison.Ordinal) ||
            modelId.StartsWith(".\\", StringComparison.Ordinal) ||
            modelId.StartsWith("..\\", StringComparison.Ordinal))
        {
            return CreateLocalModelInfo(modelId);
        }

        return CreateHuggingFaceModelInfo(modelId);
    }

    private static ModelInfo CreateLocalModelInfo(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? ".";
        var fileName = Path.GetFileName(fullPath);

        return new ModelInfo
        {
            Id = fullPath,
            AliasName = "local",
            DisplayName = $"Local: {fileName}",
            Parameters = 0,
            MaxSequenceLength = 512, // Default assumption
            SizeBytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0,
            OnnxFile = fileName,
            TokenizerFile = "tokenizer.json",
            Description = $"Local model from {directory}",
            IsMultilingual = false
        };
    }

    private static ModelInfo CreateHuggingFaceModelInfo(string modelId)
    {
        var parts = modelId.Split('/');
        var name = parts.Length > 1 ? parts[1] : modelId;

        return new ModelInfo
        {
            Id = modelId,
            AliasName = modelId,
            DisplayName = name,
            Parameters = 0,
            MaxSequenceLength = 512,
            SizeBytes = 0,
            OnnxFile = "onnx/model.onnx",
            TokenizerFile = "tokenizer.json",
            Description = $"Custom model: {modelId}",
            IsMultilingual = false
        };
    }
}
