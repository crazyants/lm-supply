namespace LMSupply.Generator;

/// <summary>
/// Legacy registry of supported ONNX models with license and configuration information.
/// Use <see cref="GeneratorModelRegistry"/> instead for new code.
/// </summary>
[Obsolete("Use GeneratorModelRegistry.Default instead. This class will be removed in a future version.")]
public static class ModelRegistry
{
    // Keep a static dict for backward-compatible GetModel()/IsRegistered()
    // These methods return null for unknown models, which the new registry doesn't do.
    private static readonly Dictionary<string, ModelInfo> _modelsById;

    static ModelRegistry()
    {
        _modelsById = new Dictionary<string, ModelInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in DefaultGeneratorModels.All)
        {
            _modelsById[model.ModelId] = model;
        }
    }

    /// <summary>
    /// Gets all registered models.
    /// </summary>
    [Obsolete("Use GeneratorModelRegistry.Default.GetAvailableModels() instead.")]
    public static IReadOnlyList<ModelInfo> GetAllModels() =>
        GeneratorModelRegistry.Default.GetAvailableModels().ToList();

    /// <summary>
    /// Gets models filtered by license tier.
    /// </summary>
    [Obsolete("Use GeneratorModelRegistry.Default.GetAvailableModels() with LINQ instead.")]
    public static IReadOnlyList<ModelInfo> GetModelsByLicense(LicenseTier tier) =>
        GeneratorModelRegistry.Default.GetAvailableModels().Where(m => m.License == tier).ToList();

    /// <summary>
    /// Gets MIT-licensed models (no usage restrictions).
    /// </summary>
    [Obsolete("Use GeneratorModelRegistry.Default.GetAvailableModels() with LINQ instead.")]
    public static IReadOnlyList<ModelInfo> GetUnrestrictedModels() =>
        GetModelsByLicense(LicenseTier.MIT);

    /// <summary>
    /// Gets model information by ID.
    /// Returns null for unknown models (unlike the new registry which throws).
    /// </summary>
    /// <param name="modelId">The model identifier (e.g., "microsoft/Phi-3.5-mini-instruct-onnx").</param>
    /// <returns>Model information if found, null otherwise.</returns>
    [Obsolete("Use GeneratorModelRegistry.Default.TryResolve() instead.")]
    public static ModelInfo? GetModel(string modelId) =>
        _modelsById.GetValueOrDefault(modelId);

    /// <summary>
    /// Checks if a model is registered.
    /// </summary>
    [Obsolete("Use GeneratorModelRegistry.Default.TryResolve() instead.")]
    public static bool IsRegistered(string modelId) =>
        _modelsById.ContainsKey(modelId);

    /// <summary>
    /// Gets models that fit within available memory.
    /// </summary>
    /// <param name="availableMemoryBytes">Available memory in bytes.</param>
    /// <param name="contextLength">Desired context length.</param>
    /// <returns>List of models that can fit in memory.</returns>
    [Obsolete("Use GeneratorModelRegistry.Default.GetAvailableModels() with memory filtering instead.")]
    public static IReadOnlyList<ModelInfo> GetModelsForMemory(long availableMemoryBytes, int contextLength = 4096)
    {
        return GeneratorModelRegistry.Default.GetAvailableModels()
            .Where(m => CanFitInMemory(m, availableMemoryBytes, contextLength))
            .OrderByDescending(m => m.ParameterCount)
            .ToList();
    }

    /// <summary>
    /// Gets the default recommended model based on hardware.
    /// </summary>
    [Obsolete("Use GeneratorModelRegistry.Default.Resolve(\"auto\") instead.")]
    public static ModelInfo GetDefaultModel()
    {
        var recommendation = HardwareDetector.GetRecommendation();
        var availableMemory = recommendation.GpuInfo.TotalMemoryBytes
            ?? recommendation.SystemMemoryBytes;

        // Prefer MIT-licensed models first
        var candidates = GetModelsForMemory(availableMemory)
            .OrderBy(m => m.License) // MIT first (lower enum value)
            .ThenByDescending(m => m.ParameterCount);

        return candidates.FirstOrDefault() ?? DefaultGeneratorModels.Phi35Mini;
    }

    private static bool CanFitInMemory(ModelInfo model, long availableMemoryBytes, int contextLength)
    {
        var config = new ModelMemoryConfig
        {
            ParameterCount = model.ParameterCount,
            NumLayers = model.NumLayers,
            HiddenSize = model.HiddenSize,
            ContextLength = contextLength,
            Quantization = model.DefaultQuantization
        };

        return MemoryEstimator.CanFitInMemory(config, availableMemoryBytes);
    }
}

/// <summary>
/// License tier classification.
/// </summary>
public enum LicenseTier
{
    /// <summary>MIT license - no restrictions, commercial use allowed.</summary>
    MIT = 0,

    /// <summary>Conditional license - restrictions apply (MAU limits, usage policies).</summary>
    Conditional = 1,

    /// <summary>Research only - not suitable for production use.</summary>
    ResearchOnly = 2
}

/// <summary>
/// Model metadata and configuration.
/// </summary>
public sealed record ModelInfo : IModelInfoBase
{
    /// <summary>
    /// Unique model identifier (HuggingFace format: org/model-name).
    /// </summary>
    public required string ModelId { get; init; }

    // IModelInfoBase implementation
    string IModelInfoBase.Id => ModelId;

    /// <summary>
    /// Gets the alias name for this model.
    /// Set by the registry for system aliases (e.g., "default", "fast").
    /// Defaults to ModelId if not explicitly set.
    /// </summary>
    public string AliasName { get; init; } = null!;

    /// <summary>
    /// Gets the model description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Human-friendly display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Total parameter count.
    /// </summary>
    public required long ParameterCount { get; init; }

    /// <summary>
    /// License classification tier.
    /// </summary>
    public required LicenseTier License { get; init; }

    /// <summary>
    /// License name (e.g., "MIT", "Llama 3.2 Community License").
    /// </summary>
    public required string LicenseName { get; init; }

    /// <summary>
    /// Description of any usage restrictions. Null for unrestricted licenses.
    /// </summary>
    public string? LicenseRestrictions { get; init; }

    /// <summary>
    /// Chat format identifier for prompt formatting.
    /// </summary>
    public required string ChatFormat { get; init; }

    /// <summary>
    /// Default quantization level for this model.
    /// </summary>
    public required Quantization DefaultQuantization { get; init; }

    /// <summary>
    /// Recommended maximum context length.
    /// </summary>
    public required int RecommendedContextLength { get; init; }

    /// <summary>
    /// Number of transformer layers.
    /// </summary>
    public required int NumLayers { get; init; }

    /// <summary>
    /// Hidden dimension size.
    /// </summary>
    public required int HiddenSize { get; init; }

    /// <summary>
    /// Subfolder within the repository containing the ONNX files.
    /// </summary>
    public string? Subfolder { get; init; }

    /// <summary>
    /// Gets memory configuration for this model.
    /// </summary>
    public ModelMemoryConfig GetMemoryConfig(int? contextLength = null) => new()
    {
        ParameterCount = ParameterCount,
        NumLayers = NumLayers,
        HiddenSize = HiddenSize,
        ContextLength = contextLength ?? RecommendedContextLength,
        Quantization = DefaultQuantization
    };

    /// <summary>
    /// Checks if this model has usage restrictions.
    /// </summary>
    public bool HasRestrictions => License != LicenseTier.MIT;
}
