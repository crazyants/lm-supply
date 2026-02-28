using System.Diagnostics;
using LMSupply.Hardware;

namespace LMSupply.Detector.Models;

/// <summary>
/// Model registry for the Detector domain.
/// </summary>
public sealed class DetectorModelRegistry : ModelRegistryBase<DetectorModelInfo>
{
    /// <summary>
    /// Gets the default registry instance with built-in models.
    /// </summary>
    public static DetectorModelRegistry Default { get; } = new(DefaultModels.All);

    /// <summary>
    /// Initializes a new registry with the specified system models.
    /// </summary>
    /// <param name="systemModels">Models to register as system defaults.</param>
    public DetectorModelRegistry(IEnumerable<DetectorModelInfo> systemModels)
        : base(systemModels) { }

    /// <summary>
    /// Gets the optimal model based on current hardware profile.
    /// Uses PerformanceTier to select appropriate model size.
    /// </summary>
    /// <remarks>
    /// Tier mapping:
    /// - Low:    RT-DETR v2 Mini-Small (15M params) - fast, lightweight
    /// - Medium: RT-DETR v2 Small (20M params) - balanced
    /// - High:   RT-DETR v2 Medium (36M params) - quality
    /// - Ultra:  RT-DETR v2 Large (42M params) - highest accuracy
    /// </remarks>
    protected override DetectorModelInfo GetAutoModel()
    {
        var tier = HardwareProfile.Current.Tier;
        Trace.TraceInformation($"[DetectorModelRegistry] Auto-selecting model for tier: {tier}");

        var model = tier switch
        {
            PerformanceTier.Ultra => DefaultModels.RtDetrV2L,
            PerformanceTier.High => DefaultModels.RtDetrV2M,
            PerformanceTier.Medium => DefaultModels.RtDetrV2S,
            _ => DefaultModels.RtDetrV2MS
        };

        return new DetectorModelInfo
        {
            Id = model.Id,
            AliasName = "auto",
            DisplayName = model.DisplayName,
            Architecture = model.Architecture,
            ParametersM = model.ParametersM,
            SizeBytes = model.SizeBytes,
            MapCoco = model.MapCoco,
            InputSize = model.InputSize,
            NumClasses = model.NumClasses,
            RequiresNms = model.RequiresNms,
            OnnxFile = model.OnnxFile,
            Description = model.Description,
            License = model.License
        };
    }

    /// <summary>
    /// Creates a fallback model info for unknown model IDs (HuggingFace repos or local paths).
    /// </summary>
    protected override DetectorModelInfo CreateFallbackModelInfo(string modelId)
    {
        Trace.TraceInformation($"[DetectorModelRegistry] Creating fallback model info for: {modelId}");

        // Check if it's a local path with an ONNX file
        if (modelId.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(modelId) ||
            modelId.StartsWith("./", StringComparison.Ordinal) ||
            modelId.StartsWith("../", StringComparison.Ordinal) ||
            modelId.StartsWith(".\\", StringComparison.Ordinal) ||
            modelId.StartsWith("..\\", StringComparison.Ordinal))
        {
            return CreateLocalModelInfo(modelId);
        }

        return CreateHuggingFaceModelInfo(modelId);
    }

    private static DetectorModelInfo CreateLocalModelInfo(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? ".";
        var fileName = Path.GetFileName(fullPath);

        return new DetectorModelInfo
        {
            Id = fullPath,
            AliasName = "local",
            DisplayName = $"Local: {fileName}",
            Architecture = "Unknown",
            ParametersM = 0,
            SizeBytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0,
            MapCoco = 0,
            InputSize = 640,
            NumClasses = 80,
            RequiresNms = false,
            OnnxFile = fileName,
            Description = $"Local model from {directory}",
            License = "Unknown"
        };
    }

    private static DetectorModelInfo CreateHuggingFaceModelInfo(string modelId)
    {
        var parts = modelId.Split('/');
        var name = parts.Length > 1 ? parts[1] : modelId;

        // Detect architecture from model ID
        var architecture = name.ToLowerInvariant() switch
        {
            var n when n.Contains("rtdetr") || n.Contains("rt-detr") => "RT-DETR",
            var n when n.Contains("yolo") => "YOLO",
            var n when n.Contains("efficientdet") => "EfficientDet",
            var n when n.Contains("detr") => "DETR",
            _ => "Unknown"
        };

        var requiresNms = architecture is not ("RT-DETR" or "DETR");

        return new DetectorModelInfo
        {
            Id = modelId,
            AliasName = modelId,
            DisplayName = name,
            Architecture = architecture,
            ParametersM = 0,
            SizeBytes = 0,
            MapCoco = 0,
            InputSize = 640,
            NumClasses = 80,
            RequiresNms = requiresNms,
            OnnxFile = "model.onnx",
            Description = $"HuggingFace model: {modelId}",
            License = "Unknown"
        };
    }
}
