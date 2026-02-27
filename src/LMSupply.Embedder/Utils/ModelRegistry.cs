using System.ComponentModel;

namespace LMSupply.Embedder.Utils;

/// <summary>
/// Legacy static facade for backward compatibility.
/// Delegates to <see cref="EmbedderModelRegistry.Default"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Obsolete("Use EmbedderModelRegistry.Default instead.")]
internal static class ModelRegistry
{
    /// <summary>
    /// Tries to get model info by model ID.
    /// </summary>
    public static bool TryGetModel(string modelId, out ModelInfo? info)
    {
        return EmbedderModelRegistry.Default.TryResolve(modelId, out info);
    }

    /// <summary>
    /// Gets all available model IDs including "auto".
    /// </summary>
    public static IEnumerable<string> GetAvailableModels()
    {
        return EmbedderModelRegistry.Default.GetAliases().Select(a => a.Name);
    }

    /// <summary>
    /// Gets all registered model information (deduplicated by RepoId).
    /// </summary>
    public static IEnumerable<ModelInfo> GetAllModels()
    {
        return EmbedderModelRegistry.Default.GetAvailableModels();
    }
}
