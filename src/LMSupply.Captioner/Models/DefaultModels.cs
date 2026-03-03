using LMSupply.Vision;

namespace LMSupply.Captioner.Models;

/// <summary>
/// Provides definitions for built-in supported captioning models.
/// </summary>
public static class DefaultModels
{
    /// <summary>
    /// Gets the default model (most stable ONNX conversion).
    /// </summary>
    public static ModelInfo Default => VitGpt2;

    /// <summary>
    /// ViT-GPT2 Image Captioning - Default model, most stable ONNX conversion.
    /// GPT-2 tokenizer, 224x224 ViT preprocessing.
    /// </summary>
    public static ModelInfo VitGpt2 { get; } = new(
        RepoId: "Xenova/vit-gpt2-image-captioning",
        AliasName: "default",
        DisplayName: "ViT-GPT2 Image Captioning",
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
        Description = "Default: ViT-GPT2, most stable ONNX conversion"
    };

    /// <summary>
    /// Same model registered under the "fast" alias.
    /// Xenova/git-base-coco (original "fast" model) became inaccessible (401 Unauthorized) circa 2026-03.
    /// </summary>
    public static ModelInfo VitGpt2Fast { get; } = VitGpt2 with
    {
        AliasName = "fast",
        Description = "Fast: ViT-GPT2 (same as default — original GIT-Base COCO model no longer accessible)"
    };

    /// <summary>
    /// Gets all built-in models.
    /// Note: Xenova/blip-image-captioning-base (quality) and Xenova/blip-image-captioning-large (large)
    /// were removed because their HuggingFace repos became inaccessible (401 Unauthorized) circa 2026-03.
    /// </summary>
    public static IReadOnlyList<ModelInfo> All { get; } =
    [
        VitGpt2,      // default
        VitGpt2Fast,  // fast
    ];
}
