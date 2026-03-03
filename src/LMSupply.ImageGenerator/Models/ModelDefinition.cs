namespace LMSupply.ImageGenerator.Models;

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
