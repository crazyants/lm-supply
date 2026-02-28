namespace LMSupply.Ocr.Models;

/// <summary>
/// Provides definitions for built-in OCR text detection models.
/// </summary>
internal static class DefaultDetectionModels
{
    private const string DefaultRepoId = "monkt/paddleocr-onnx";

    /// <summary>
    /// PaddleOCR v3 Detection (DBNet) — the primary built-in detection model.
    /// </summary>
    public static DetectionModelInfo DbNetV3 { get; } = new(
        RepoId: DefaultRepoId,
        AliasName: "dbnet-v3",
        DisplayName: "PaddleOCR v3 Detection (DBNet)",
        ModelFile: "det.onnx",
        InputWidth: 960,
        InputHeight: 960)
    {
        Subfolder = "detection/v3"
    };

    /// <summary>
    /// Same model registered under the "default" alias.
    /// </summary>
    public static DetectionModelInfo DbNetV3Default { get; } = DbNetV3 with { AliasName = "default" };

    /// <summary>
    /// Gets all built-in detection models.
    /// Primary alias first so it becomes the canonical entry in GetAvailableModels().
    /// </summary>
    public static IReadOnlyList<DetectionModelInfo> All { get; } =
    [
        DbNetV3,          // dbnet-v3 (primary)
        DbNetV3Default,   // default (alias)
    ];
}
