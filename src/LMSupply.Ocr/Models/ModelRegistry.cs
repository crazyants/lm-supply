using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace LMSupply.Ocr.Models;

/// <summary>
/// Legacy static facade for backward compatibility.
/// Delegates to <see cref="OcrDetectionModelRegistry.Default"/> and
/// <see cref="OcrRecognitionModelRegistry.Default"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Obsolete("Use OcrDetectionModelRegistry.Default and OcrRecognitionModelRegistry.Default instead.")]
public static class ModelRegistry
{
    #region Detection Models

    /// <summary>
    /// Registers a detection model with the registry.
    /// </summary>
    public static void RegisterDetectionModel(DetectionModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);
        // User-level registration: register as an alias pointing to model ID
        OcrDetectionModelRegistry.Default.RegisterAlias(model.AliasName, model.RepoId);
    }

    /// <summary>
    /// Registers an alias for an existing detection model.
    /// </summary>
    public static void RegisterDetectionAlias(string alias, string existingAlias)
    {
        OcrDetectionModelRegistry.Default.RegisterAlias(alias, existingAlias);
    }

    /// <summary>
    /// Tries to get detection model info by alias.
    /// </summary>
    public static bool TryGetDetectionModel(string aliasOrId, [NotNullWhen(true)] out DetectionModelInfo? modelInfo)
    {
        return OcrDetectionModelRegistry.Default.TryResolve(aliasOrId, out modelInfo);
    }

    /// <summary>
    /// Gets detection model info by alias.
    /// </summary>
    public static DetectionModelInfo GetDetectionModel(string aliasOrId)
    {
        return OcrDetectionModelRegistry.Default.Resolve(aliasOrId);
    }

    /// <summary>
    /// Gets a list of available detection model aliases.
    /// </summary>
    public static IEnumerable<string> GetAvailableDetectionModels()
    {
        return OcrDetectionModelRegistry.Default.GetAvailableModels()
            .Select(m => m.AliasName)
            .Distinct()
            .Order();
    }

    /// <summary>
    /// Gets all registered detection models.
    /// </summary>
    public static IEnumerable<DetectionModelInfo> GetAllDetectionModels()
    {
        return OcrDetectionModelRegistry.Default.GetAvailableModels();
    }

    #endregion

    #region Recognition Models

    /// <summary>
    /// Registers a recognition model with the registry.
    /// </summary>
    public static void RegisterRecognitionModel(RecognitionModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);
        // User-level registration: register as an alias pointing to model ID
        OcrRecognitionModelRegistry.Default.RegisterAlias(model.AliasName, model.RepoId);
    }

    /// <summary>
    /// Registers an alias for an existing recognition model.
    /// </summary>
    public static void RegisterRecognitionAlias(string alias, string existingAlias)
    {
        OcrRecognitionModelRegistry.Default.RegisterAlias(alias, existingAlias);
    }

    /// <summary>
    /// Tries to get recognition model info by alias.
    /// </summary>
    public static bool TryGetRecognitionModel(string aliasOrId, [NotNullWhen(true)] out RecognitionModelInfo? modelInfo)
    {
        return OcrRecognitionModelRegistry.Default.TryResolve(aliasOrId, out modelInfo);
    }

    /// <summary>
    /// Gets recognition model info by alias.
    /// </summary>
    public static RecognitionModelInfo GetRecognitionModel(string aliasOrId)
    {
        return OcrRecognitionModelRegistry.Default.Resolve(aliasOrId);
    }

    /// <summary>
    /// Gets recognition model for a specific language code.
    /// Falls back to English if language not found.
    /// </summary>
    public static RecognitionModelInfo GetRecognitionModelForLanguage(string languageCode)
    {
        return OcrRecognitionModelRegistry.Default.ResolveForLanguage(languageCode);
    }

    /// <summary>
    /// Gets a list of available recognition model aliases.
    /// </summary>
    public static IEnumerable<string> GetAvailableRecognitionModels()
    {
        return OcrRecognitionModelRegistry.Default.GetAvailableModels()
            .Select(m => m.AliasName)
            .Distinct()
            .Order();
    }

    /// <summary>
    /// Gets a list of supported language codes.
    /// </summary>
    public static IEnumerable<string> GetSupportedLanguages()
    {
        return OcrRecognitionModelRegistry.Default.GetSupportedLanguages();
    }

    /// <summary>
    /// Gets all registered recognition models.
    /// </summary>
    public static IEnumerable<RecognitionModelInfo> GetAllRecognitionModels()
    {
        return OcrRecognitionModelRegistry.Default.GetAvailableModels();
    }

    #endregion
}
