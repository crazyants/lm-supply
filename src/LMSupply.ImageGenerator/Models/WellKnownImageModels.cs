namespace LMSupply.ImageGenerator.Models;

/// <summary>
/// Legacy registry of well-known image generation models with pre-configured settings.
/// Use <see cref="ImageGeneratorModelRegistry"/> instead.
/// </summary>
[Obsolete("Use ImageGeneratorModelRegistry.Default instead. This class will be removed in a future version.")]
public static class WellKnownImageModels
{
    /// <summary>
    /// Resolves a model alias or ID to a full model definition.
    /// </summary>
    [Obsolete("Use ImageGeneratorModelRegistry.Default.Resolve() instead.")]
    public static ModelDefinition Resolve(string modelIdOrAlias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelIdOrAlias);

        if (ImageGeneratorModelRegistry.Default.TryResolve(modelIdOrAlias, out var info))
        {
            return ImageGeneratorModelRegistry.ToModelDefinition(info!);
        }

        // Treat as a direct HuggingFace repo ID with default settings
        return new ModelDefinition(modelIdOrAlias, null, 4, 1.0f);
    }

    /// <summary>
    /// Gets all available model aliases.
    /// </summary>
    [Obsolete("Use ImageGeneratorModelRegistry.Default.GetAliases() instead.")]
    public static IReadOnlyCollection<string> GetAliases()
    {
        return ImageGeneratorModelRegistry.Default.GetAliases()
            .Select(a => a.Name)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Checks if the given string is a known model alias.
    /// </summary>
    [Obsolete("Use ImageGeneratorModelRegistry.Default.TryResolve() instead.")]
    public static bool IsAlias(string modelIdOrAlias)
    {
        // Only return true for system/user aliases, not for HF repo fallbacks
        var aliases = ImageGeneratorModelRegistry.Default.GetAliases();
        return aliases.Any(a => a.Name.Equals(modelIdOrAlias, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Definition of a known image generation model.
/// </summary>
/// <param name="RepoId">HuggingFace repository ID.</param>
/// <param name="FriendlyName">Human-readable model name.</param>
/// <param name="RecommendedSteps">Recommended number of inference steps.</param>
/// <param name="RecommendedGuidanceScale">Recommended guidance scale.</param>
public readonly record struct ModelDefinition(
    string RepoId,
    string? FriendlyName,
    int RecommendedSteps,
    float RecommendedGuidanceScale);
