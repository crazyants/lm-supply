using System.Diagnostics;

namespace LMSupply.Ocr.Models;

/// <summary>
/// Model registry for OCR text detection models.
/// </summary>
public sealed class OcrDetectionModelRegistry : ModelRegistryBase<DetectionModelInfo>
{
    /// <summary>
    /// Gets the default registry instance with built-in detection models.
    /// </summary>
    public static OcrDetectionModelRegistry Default { get; } = new(DefaultDetectionModels.All);

    /// <summary>
    /// Initializes a new registry with the specified system models.
    /// </summary>
    /// <param name="systemModels">Models to register as system defaults.</param>
    public OcrDetectionModelRegistry(IEnumerable<DetectionModelInfo> systemModels)
        : base(systemModels) { }

    /// <summary>
    /// Returns the default detection model (DBNet v3) for the "auto" alias.
    /// OCR detection has only one built-in model, so no hardware-based selection is needed.
    /// </summary>
    protected override DetectionModelInfo GetAutoModel()
    {
        Trace.TraceInformation("[OcrDetectionModelRegistry] Auto-selecting default detection model");
        return DefaultDetectionModels.DbNetV3 with { AliasName = "auto" };
    }

    /// <summary>
    /// Creates a fallback model info for unknown model IDs (HuggingFace repos or local paths).
    /// </summary>
    protected override DetectionModelInfo CreateFallbackModelInfo(string modelId)
    {
        Trace.TraceInformation($"[OcrDetectionModelRegistry] Creating fallback model info for: {modelId}");

        return new DetectionModelInfo(
            RepoId: modelId,
            AliasName: modelId,
            DisplayName: $"Custom detection model: {modelId}",
            ModelFile: "det.onnx",
            InputWidth: 960,
            InputHeight: 960);
    }
}
