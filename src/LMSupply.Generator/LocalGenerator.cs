using LMSupply.Generator.Abstractions;
using LMSupply.Hardware;
using LMSupply.Runtime;

namespace LMSupply.Generator;

/// <summary>
/// Factory class for creating local text generation models.
/// </summary>
public static class LocalGenerator
{
    /// <summary>
    /// Default model to use when no model is specified.
    /// Microsoft Phi-4 Mini (MIT license), 3.8B params, 16K context.
    /// </summary>
    public const string DefaultModel = "microsoft/Phi-4-mini-instruct-onnx";

    /// <summary>
    /// Gets the model registry for the Generator domain.
    /// Provides access to model resolution, alias management, and model enumeration.
    /// </summary>
    public static IModelRegistry<ModelInfo> Registry => GeneratorModelRegistry.Default;

    /// <summary>
    /// Shared pool for named model management. Supports GetOrLoadAsync / UnloadAsync by model ID.
    /// </summary>
    public static GeneratorPool Pool { get; } = new(Internal.DefaultGeneratorFactory.Instance);

    /// <summary>
    /// Loads a text generator from a HuggingFace model repository.
    /// Supports "auto" alias which selects optimal model based on hardware.
    /// </summary>
    /// <param name="modelId">The HuggingFace model identifier (e.g., "microsoft/Phi-3.5-mini-instruct-onnx") or "auto".</param>
    /// <param name="options">Model loading options.</param>
    /// <param name="progress">Progress callback for model downloading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A text generator instance.</returns>
    /// <remarks>
    /// When "auto" is specified, the model is selected based on hardware:
    /// - Low tier (GPU &lt;4GB): Llama-3.2-1B (1B params)
    /// - Medium tier (GPU 4-8GB): Phi-3.5-mini (3.8B params)
    /// - High tier (GPU 8-16GB): Phi-4-mini (3.8B, 16K context)
    /// - Ultra tier (GPU 16GB+): Phi-4 (14B params)
    /// </remarks>
    public static Task<IGeneratorModel> LoadAsync(
        string modelId,
        GeneratorOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        options ??= new GeneratorOptions();

        // Parse variant qualifier (e.g., "default:fp16" → modelId="default", hint="fp16")
        var (baseId, qualifier) = LMSupplyOptionsBase.SplitQualifier(modelId);
        modelId = baseId;
        options.QuantizationHint ??= qualifier;

        // Handle "auto" — platform-based format selection
        if (modelId.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return LoadAutoAsync(options, progress, cancellationToken);
        }

        // Handle standard aliases via the registry
        if (GeneratorModelRegistry.Default.TryResolve(modelId, out var resolvedModel))
        {
            modelId = resolvedModel!.ModelId;
        }

        // Check if it's a local file path (e.g., C:\models\model.gguf or /path/to/model.gguf)
        if (File.Exists(modelId))
        {
            return Internal.GeneratorModelLoader.LoadFromPathAsync(modelId, options, modelId);
        }

        // Check if it's a local directory path
        if (Directory.Exists(modelId))
        {
            return Internal.GeneratorModelLoader.LoadFromPathAsync(modelId, options, modelId);
        }

        return Internal.GeneratorModelLoader.LoadAsync(modelId, options, progress, cancellationToken);
    }

    /// <summary>
    /// Loads a text generator from a local model path.
    /// </summary>
    /// <param name="modelPath">The path to the local model directory or GGUF file.</param>
    /// <param name="options">Model loading options.</param>
    /// <returns>A text generator instance.</returns>
    public static Task<IGeneratorModel> LoadFromPathAsync(
        string modelPath,
        GeneratorOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        // Support both directory (ONNX) and file (GGUF) paths
        if (!Directory.Exists(modelPath) && !File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Model path not found: {modelPath}");
        }

        options ??= new GeneratorOptions();

        return Internal.GeneratorModelLoader.LoadFromPathAsync(modelPath, options);
    }

    /// <summary>
    /// Loads a text generator using the default model.
    /// </summary>
    /// <param name="options">Model loading options.</param>
    /// <param name="progress">Progress callback for model downloading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A text generator instance.</returns>
    public static Task<IGeneratorModel> LoadDefaultAsync(
        GeneratorOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return LoadAsync(DefaultModel, options, progress, cancellationToken);
    }

    /// <summary>
    /// Auto-selects the optimal model based on hardware platform.
    /// GGUF for most environments (CPU, CUDA, Metal).
    /// ONNX only for Windows DirectML (non-NVIDIA) or NPU.
    /// </summary>
    private static Task<IGeneratorModel> LoadAutoAsync(
        GeneratorOptions options,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var profile = HardwareProfile.Current;
        var useOnnx = profile.RecommendedProvider == ExecutionProvider.DirectML &&
                      profile.GpuInfo.Vendor != GpuVendor.Nvidia;

        if (useOnnx)
        {
            // ONNX path: DirectML/NPU advantage
            var model = GeneratorModelRegistry.Default.Resolve("auto");
            return Internal.GeneratorModelLoader.LoadAsync(
                model.ModelId, options, progress, cancellationToken);
        }
        else
        {
            // GGUF path: CPU, CUDA, Metal, Linux — all go here
            var model = Internal.Llama.GgufModelRegistry.GetAutoModel();
            return Internal.GeneratorModelLoader.LoadAsync(
                model.RepoId, options, progress, cancellationToken);
        }
    }
}
