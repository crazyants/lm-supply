namespace LMSupply.Generator;

/// <summary>
/// Provides definitions for built-in supported generator models.
/// Updated: 2025-01 based on HuggingFace ONNX availability and benchmarks.
/// </summary>
public static class DefaultGeneratorModels
{
    /// <summary>
    /// Gets the default model (MIT license, best balance).
    /// </summary>
    public static ModelInfo Default => Phi4Mini;

    // ===== Tier 1: MIT License - No restrictions =====

    /// <summary>
    /// Microsoft Phi-4 Mini - Default balanced model (MIT license).
    /// 3.8B parameters, 16K context, excellent reasoning for its size.
    /// Released: 2025-01, successor to Phi-3.5 Mini.
    /// </summary>
    public static ModelInfo Phi4Mini { get; } = new()
    {
        ModelId = "microsoft/Phi-4-mini-instruct-onnx",
        AliasName = "default",
        DisplayName = "Phi-4 Mini",
        Description = "Default: Phi-4 Mini, 3.8B params, MIT, 16K context",
        ParameterCount = 3_800_000_000,
        License = LicenseTier.MIT,
        LicenseName = "MIT",
        ChatFormat = "phi3",
        DefaultQuantization = Quantization.Quant4,
        RecommendedContextLength = 16384,
        NumLayers = 32,
        HiddenSize = 3072,
        Subfolder = "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4"
    };

    /// <summary>
    /// Microsoft Phi-3.5 Mini - Legacy MIT model.
    /// 3.8B parameters, 128K context support, predecessor to Phi-4 Mini.
    /// </summary>
    public static ModelInfo Phi35Mini { get; } = new()
    {
        ModelId = "microsoft/Phi-3.5-mini-instruct-onnx",
        AliasName = "phi-3.5-mini",
        DisplayName = "Phi-3.5 Mini",
        Description = "Phi-3.5 Mini, 3.8B params, MIT, 128K context support",
        ParameterCount = 3_800_000_000,
        License = LicenseTier.MIT,
        LicenseName = "MIT",
        ChatFormat = "phi3",
        DefaultQuantization = Quantization.Quant4,
        RecommendedContextLength = 4096,
        NumLayers = 32,
        HiddenSize = 3072,
        Subfolder = "cpu_and_mobile/cpu-int4-awq-block-128-acc-level-4"
    };

    /// <summary>
    /// Microsoft Phi-4 - Quality model (MIT license).
    /// 14B parameters, 16K context, highest quality reasoning.
    /// Released: 2024-12, state-of-the-art for its size.
    /// </summary>
    public static ModelInfo Phi4 { get; } = new()
    {
        ModelId = "microsoft/phi-4-onnx",
        AliasName = "quality",
        DisplayName = "Phi-4",
        Description = "Quality: Phi-4, 14B params, MIT, highest quality reasoning",
        ParameterCount = 14_000_000_000,
        License = LicenseTier.MIT,
        LicenseName = "MIT",
        ChatFormat = "phi3",
        DefaultQuantization = Quantization.Quant4,
        RecommendedContextLength = 8192,
        NumLayers = 40,
        HiddenSize = 5120,
        Subfolder = "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4"
    };

    // ===== Tier 2: Conditional - Usage restrictions apply =====

    /// <summary>
    /// Meta Llama 3.2 1B - Fast/small model.
    /// 1B parameters, fast inference, good for simple tasks.
    /// Note: Llama Community License (700M MAU limit).
    /// </summary>
    public static ModelInfo Llama321B { get; } = new()
    {
        ModelId = "onnx-community/Llama-3.2-1B-Instruct-ONNX",
        AliasName = "fast",
        DisplayName = "Llama 3.2 1B",
        Description = "Fast: Llama 3.2 1B, fast inference, Llama license",
        ParameterCount = 1_000_000_000,
        License = LicenseTier.Conditional,
        LicenseName = "Llama 3.2 Community License",
        LicenseRestrictions = "700M MAU limit for commercial use",
        ChatFormat = "llama3",
        DefaultQuantization = Quantization.Quant4,
        RecommendedContextLength = 4096,
        NumLayers = 16,
        HiddenSize = 2048,
        Subfolder = "onnx"
    };

    /// <summary>
    /// Meta Llama 3.2 3B - Medium-large model.
    /// 3B parameters, good accuracy for complex reasoning.
    /// Note: Llama Community License (700M MAU limit).
    /// </summary>
    public static ModelInfo Llama323B { get; } = new()
    {
        ModelId = "onnx-community/Llama-3.2-3B-Instruct-ONNX",
        AliasName = "llama-3.2-3b",
        DisplayName = "Llama 3.2 3B",
        Description = "Llama 3.2 3B, good accuracy, Llama license",
        ParameterCount = 3_000_000_000,
        License = LicenseTier.Conditional,
        LicenseName = "Llama 3.2 Community License",
        LicenseRestrictions = "700M MAU limit for commercial use",
        ChatFormat = "llama3",
        DefaultQuantization = Quantization.Quant4,
        RecommendedContextLength = 4096,
        NumLayers = 28,
        HiddenSize = 3072,
        Subfolder = "onnx"
    };

    /// <summary>
    /// Google Gemma 2 2B - Multilingual model.
    /// 2B parameters, good multilingual support.
    /// Note: Gemma Terms of Use, Prohibited Use Policy applies.
    /// </summary>
    public static ModelInfo Gemma22B { get; } = new()
    {
        ModelId = "google/gemma-2-2b-it-onnx",
        AliasName = "gemma-2-2b",
        DisplayName = "Gemma 2 2B",
        Description = "Gemma 2 2B, multilingual, Gemma license",
        ParameterCount = 2_000_000_000,
        License = LicenseTier.Conditional,
        LicenseName = "Gemma Terms of Use",
        LicenseRestrictions = "Prohibited Use Policy applies",
        ChatFormat = "gemma",
        DefaultQuantization = Quantization.Quant4,
        RecommendedContextLength = 4096,
        NumLayers = 26,
        HiddenSize = 2304,
        Subfolder = "onnx"
    };

    /// <summary>
    /// Gets all built-in models.
    /// </summary>
    public static IReadOnlyList<ModelInfo> All { get; } =
    [
        Phi4Mini,   // default
        Phi35Mini,  // phi-3.5-mini
        Phi4,       // quality
        Llama321B,  // fast
        Llama323B,  // llama-3.2-3b
        Gemma22B    // gemma-2-2b
    ];
}
