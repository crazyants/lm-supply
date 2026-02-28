using System.Diagnostics;
using LMSupply.Hardware;

namespace LMSupply.Segmenter.Models;

/// <summary>
/// Model registry for the Segmenter domain.
/// </summary>
public sealed class SegmenterModelRegistry : ModelRegistryBase<SegmenterModelInfo>
{
    /// <summary>
    /// Gets the default registry instance with built-in models.
    /// </summary>
    public static SegmenterModelRegistry Default { get; } = new(DefaultModels.All);

    /// <summary>
    /// Initializes a new registry with the specified system models.
    /// </summary>
    /// <param name="systemModels">Models to register as system defaults.</param>
    public SegmenterModelRegistry(IEnumerable<SegmenterModelInfo> systemModels)
        : base(systemModels) { }

    /// <summary>
    /// Gets the optimal model based on current hardware profile.
    /// Uses PerformanceTier to select appropriate model size.
    /// </summary>
    /// <remarks>
    /// Tier mapping (semantic segmentation focus):
    /// - Low:    MediaPipe Selfie (0.7M params) - ultra lightweight, fast
    /// - Medium: SegFormer-B0 (3.7M params) - balanced
    /// - High:   MaskFormer ResNet50 (44M params) - quality
    /// - Ultra:  MaskFormer ResNet50 (44M params) - highest accuracy
    /// </remarks>
    protected override SegmenterModelInfo GetAutoModel()
    {
        var tier = HardwareProfile.Current.Tier;
        Trace.TraceInformation($"[SegmenterModelRegistry] Auto-selecting model for tier: {tier}");

        var model = tier switch
        {
            PerformanceTier.Ultra or PerformanceTier.High => DefaultModels.MaskFormerResNet50,
            PerformanceTier.Medium => DefaultModels.SegFormerB0,
            _ => DefaultModels.MediaPipeSelfie
        };

        return new SegmenterModelInfo
        {
            Id = model.Id,
            AliasName = "auto",
            DisplayName = model.DisplayName,
            Architecture = model.Architecture,
            ParametersM = model.ParametersM,
            SizeBytes = model.SizeBytes,
            MIoU = model.MIoU,
            InputSize = model.InputSize,
            NumClasses = model.NumClasses,
            OnnxFile = model.OnnxFile,
            EncoderFile = model.EncoderFile,
            DecoderFile = model.DecoderFile,
            Dataset = model.Dataset,
            Description = model.Description,
            License = model.License
        };
    }

    /// <summary>
    /// Creates a fallback model info for unknown model IDs (HuggingFace repos or local paths).
    /// </summary>
    protected override SegmenterModelInfo CreateFallbackModelInfo(string modelId)
    {
        Trace.TraceInformation($"[SegmenterModelRegistry] Creating fallback model info for: {modelId}");

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

    private static SegmenterModelInfo CreateLocalModelInfo(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? ".";
        var fileName = Path.GetFileName(fullPath);

        return new SegmenterModelInfo
        {
            Id = fullPath,
            AliasName = "local",
            DisplayName = $"Local: {fileName}",
            Architecture = "Unknown",
            ParametersM = 0,
            SizeBytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0,
            MIoU = 0,
            InputSize = 512,
            NumClasses = 150,
            OnnxFile = fileName,
            Dataset = "Unknown",
            Description = $"Local model from {directory}",
            License = "Unknown"
        };
    }

    private static SegmenterModelInfo CreateHuggingFaceModelInfo(string modelId)
    {
        var parts = modelId.Split('/');
        var name = parts.Length > 1 ? parts[1] : modelId;

        // Detect architecture from model ID
        var architecture = name.ToLowerInvariant() switch
        {
            var n when n.Contains("segformer") => "SegFormer",
            var n when n.Contains("deeplabv3") || n.Contains("deeplab") => "DeepLabV3+",
            var n when n.Contains("mask2former") => "Mask2Former",
            var n when n.Contains("sam") => "SAM",
            _ => "Unknown"
        };

        // Try to detect number of classes from model name
        var numClasses = name.ToLowerInvariant() switch
        {
            var n when n.Contains("ade") => 150,
            var n when n.Contains("cityscapes") => 19,
            var n when n.Contains("coco") => 171,
            _ => 150
        };

        // Detect input size from model name
        var inputSize = 512;
        if (name.Contains("640"))
            inputSize = 640;
        else if (name.Contains("1024"))
            inputSize = 1024;

        return new SegmenterModelInfo
        {
            Id = modelId,
            AliasName = modelId,
            DisplayName = name,
            Architecture = architecture,
            ParametersM = 0,
            SizeBytes = 0,
            MIoU = 0,
            InputSize = inputSize,
            NumClasses = numClasses,
            OnnxFile = "model.onnx",
            Dataset = "Unknown",
            Description = $"HuggingFace model: {modelId}",
            License = "Unknown"
        };
    }
}
