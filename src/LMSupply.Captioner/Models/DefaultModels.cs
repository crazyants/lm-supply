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
    /// GIT Base COCO - Fast model, efficient architecture.
    /// BERT tokenizer, 224x224 ViT preprocessing.
    /// </summary>
    public static ModelInfo GitBase { get; } = new(
        RepoId: "Xenova/git-base-coco",
        AliasName: "fast",
        DisplayName: "GIT Base COCO",
        EncoderFile: "encoder_model.onnx",
        DecoderFile: "decoder_model_merged.onnx",
        TokenizerType: TokenizerType.Bert,
        PreprocessProfile: PreprocessProfile.ViTGpt2,
        SupportsVqa: false,
        VocabSize: 30522,
        BosTokenId: 101,
        EosTokenId: 102,
        PadTokenId: 0)
    {
        Subfolder = "onnx",
        Description = "Fast: GIT Base, efficient architecture"
    };

    /// <summary>
    /// BLIP Image Captioning Base - Quality model, 384x384 resolution.
    /// BERT tokenizer, BLIP preprocessing.
    /// </summary>
    public static ModelInfo BlipBase { get; } = new(
        RepoId: "Xenova/blip-image-captioning-base",
        AliasName: "quality",
        DisplayName: "BLIP Image Captioning Base",
        EncoderFile: "vision_model.onnx",
        DecoderFile: "text_decoder_model_merged.onnx",
        TokenizerType: TokenizerType.Bert,
        PreprocessProfile: PreprocessProfile.Blip,
        SupportsVqa: false,
        VocabSize: 30524,
        BosTokenId: 30522,
        EosTokenId: 102,
        PadTokenId: 0)
    {
        Subfolder = "onnx",
        Description = "Quality: BLIP Base, 384x384 resolution"
    };

    /// <summary>
    /// BLIP Image Captioning Large - Large model, higher quality.
    /// BERT tokenizer, BLIP preprocessing.
    /// </summary>
    public static ModelInfo BlipLarge { get; } = new(
        RepoId: "Xenova/blip-image-captioning-large",
        AliasName: "large",
        DisplayName: "BLIP Image Captioning Large",
        EncoderFile: "vision_model.onnx",
        DecoderFile: "text_decoder_model_merged.onnx",
        TokenizerType: TokenizerType.Bert,
        PreprocessProfile: PreprocessProfile.Blip,
        SupportsVqa: false,
        VocabSize: 30524,
        BosTokenId: 30522,
        EosTokenId: 102,
        PadTokenId: 0)
    {
        Subfolder = "onnx",
        Description = "Large: BLIP Large, higher quality"
    };

    /// <summary>
    /// Gets all built-in models.
    /// </summary>
    public static IReadOnlyList<ModelInfo> All { get; } =
    [
        VitGpt2,    // default
        GitBase,    // fast
        BlipBase,   // quality
        BlipLarge   // large
    ];
}
