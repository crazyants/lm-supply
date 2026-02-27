using System.Diagnostics.CodeAnalysis;

namespace LMSupply.Captioner.Models;

/// <summary>
/// Legacy registry of known captioning models and their configurations.
/// Use <see cref="CaptionerModelRegistry"/> instead.
/// </summary>
[Obsolete("Use CaptionerModelRegistry.Default instead. This class will be removed in a future version.")]
public static class ModelRegistry
{
    /// <summary>
    /// Tries to get model info by alias or repo ID.
    /// </summary>
    [Obsolete("Use CaptionerModelRegistry.Default.TryResolve() instead.")]
    public static bool TryGetModel(string modelIdOrAlias, [NotNullWhen(true)] out ModelInfo? modelInfo)
    {
        return CaptionerModelRegistry.Default.TryResolve(modelIdOrAlias, out modelInfo);
    }

    /// <summary>
    /// Gets model info by alias or repo ID.
    /// </summary>
    /// <exception cref="KeyNotFoundException">If the model is not found.</exception>
    [Obsolete("Use CaptionerModelRegistry.Default.Resolve() instead.")]
    public static ModelInfo GetModel(string modelIdOrAlias)
    {
        return CaptionerModelRegistry.Default.Resolve(modelIdOrAlias);
    }

    /// <summary>
    /// Gets a list of available model identifiers.
    /// </summary>
    [Obsolete("Use CaptionerModelRegistry.Default.GetAliases() instead.")]
    public static IEnumerable<string> GetAvailableModels()
    {
        return CaptionerModelRegistry.Default.GetAliases().Select(a => a.Name);
    }

    /// <summary>
    /// Gets all registered model information.
    /// </summary>
    [Obsolete("Use CaptionerModelRegistry.Default.GetAvailableModels() instead.")]
    public static IEnumerable<ModelInfo> GetAllModels()
    {
        return CaptionerModelRegistry.Default.GetAvailableModels();
    }
}
