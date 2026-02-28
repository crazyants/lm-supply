using System.Diagnostics;
using LMSupply.Hardware;
using LMSupply.Vision;

namespace LMSupply.Captioner.Models;

/// <summary>
/// Model registry for the Captioner domain.
/// </summary>
public sealed class CaptionerModelRegistry : ModelRegistryBase<ModelInfo>
{
    /// <summary>
    /// Gets the default registry instance with built-in models.
    /// </summary>
    public static CaptionerModelRegistry Default { get; } = new(DefaultModels.All);

    /// <summary>
    /// Initializes a new registry with the specified system models.
    /// </summary>
    /// <param name="systemModels">Models to register as system defaults.</param>
    public CaptionerModelRegistry(IEnumerable<ModelInfo> systemModels)
        : base(systemModels) { }

    /// <summary>
    /// Gets the optimal model based on current hardware profile.
    /// Uses PerformanceTier to select appropriate model size.
    /// </summary>
    /// <remarks>
    /// Tier mapping:
    /// - Low:    vit-gpt2 - lightweight, most stable
    /// - Medium: blip-base - balanced quality, 384x384
    /// - High:   blip-large - higher quality
    /// - Ultra:  blip-large - higher quality
    /// </remarks>
    protected override ModelInfo GetAutoModel()
    {
        var tier = HardwareProfile.Current.Tier;
        Trace.TraceInformation($"[CaptionerModelRegistry] Auto-selecting model for tier: {tier}");

        var model = tier switch
        {
            PerformanceTier.Ultra or PerformanceTier.High => DefaultModels.BlipLarge,
            PerformanceTier.Medium => DefaultModels.BlipBase,
            _ => DefaultModels.VitGpt2
        };

        return model with { AliasName = "auto" };
    }

    /// <summary>
    /// Creates a fallback model info for unknown model IDs (HuggingFace repos or local paths).
    /// </summary>
    protected override ModelInfo CreateFallbackModelInfo(string modelId)
    {
        Trace.TraceInformation($"[CaptionerModelRegistry] Creating fallback model info for: {modelId}");

        var parts = modelId.Split('/');
        var name = parts.Length > 1 ? parts[1] : modelId;

        // Default to ViT-GPT2-compatible settings for unknown models
        return new ModelInfo(
            RepoId: modelId,
            AliasName: modelId,
            DisplayName: name,
            EncoderFile: "encoder_model.onnx",
            DecoderFile: "decoder_model_merged.onnx",
            TokenizerType: TokenizerType.Gpt2,
            PreprocessProfile: PreprocessProfile.ViTGpt2,
            SupportsVqa: false,
            VocabSize: 50257,
            BosTokenId: 50256,
            EosTokenId: 50256,
            PadTokenId: 50256)
        {
            Subfolder = "onnx",
            Description = $"Custom model: {modelId}"
        };
    }
}
