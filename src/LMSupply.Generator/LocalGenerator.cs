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
    /// Gets the model registry for the Generator domain.
    /// Provides access to model resolution, alias management, and model enumeration.
    /// </summary>
    public static IModelRegistry<ModelInfo> Registry => GeneratorModelRegistry.Default;

    /// <summary>
    /// Shared pool for named model management. Supports GetOrLoadAsync / UnloadAsync by model ID.
    /// </summary>
    public static GeneratorPool Pool { get; } = new(Internal.DefaultGeneratorFactory.Instance);

    /// <summary>
    /// Loads a text generator from a HuggingFace model repository or alias.
    /// </summary>
    /// <param name="modelId">
    /// One of:
    /// <list type="bullet">
    ///   <item><c>"default"</c> / <c>"auto"</c> — hardware-aware selection (see remarks).</item>
    ///   <item>A GGUF alias (<c>"gguf:default"</c>, <c>"gguf:fast"</c>, <c>"gguf:quality"</c>, etc.).</item>
    ///   <item>An ONNX alias (<c>"phi-4-mini"</c>, <c>"fast"</c>, <c>"quality"</c>, <c>"phi-3.5-mini"</c>).</item>
    ///   <item>A HuggingFace repo ID (e.g., <c>"microsoft/Phi-4-mini-instruct-onnx"</c>).</item>
    ///   <item>A local file path (GGUF) or directory (ONNX).</item>
    /// </list>
    /// </param>
    /// <param name="options">Model loading options.</param>
    /// <param name="progress">Progress callback for model downloading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A text generator instance.</returns>
    /// <remarks>
    /// For <c>"default"</c> and <c>"auto"</c>, the backend and model are selected from the host:
    /// <list type="bullet">
    ///   <item>NVIDIA GPU / CPU / macOS / Linux → GGUF via llama.cpp (Gemma 4 by default, VRAM-aware).</item>
    ///   <item>Windows with DirectML and a non-NVIDIA GPU → ONNX (Phi-4 Mini).</item>
    /// </list>
    /// The selection is logged via <c>Trace.TraceInformation</c> with a <c>[LocalGenerator.auto]</c> prefix.
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

        // "default" and "auto" both delegate to hardware-aware selection.
        if (modelId.Equals("default", StringComparison.OrdinalIgnoreCase) ||
            modelId.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return LoadAutoAsync(options, progress, cancellationToken);
        }

        // Handle other standard aliases via the registry
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
        return LoadAsync("default", options, progress, cancellationToken);
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

        string selectedModelId;

        if (useOnnx)
        {
            var model = GeneratorModelRegistry.Default.Resolve("auto");
            selectedModelId = model.ModelId;
            LogOnnxAutoSelection(profile, model.ModelId);
        }
        else
        {
            var selection = Internal.Llama.GgufModelRegistry.GetAutoSelection(profile.GpuInfo);
            selectedModelId = selection.Selected.RepoId;
            LogGgufAutoSelection(profile, selection);
        }

        return Internal.GeneratorModelLoader.LoadAsync(
            selectedModelId, options, progress, cancellationToken);
    }

    private static void LogOnnxAutoSelection(HardwareProfile profile, string modelId)
    {
        var vramMb = VramBudget.GetAvailableBytes(profile.GpuInfo) / (1024 * 1024);
        System.Diagnostics.Trace.TraceInformation(
            $"[LocalGenerator.auto] Provider={profile.RecommendedProvider}, " +
            $"GPU={profile.GpuInfo.Vendor} {profile.GpuInfo.DeviceName ?? "n/a"}, " +
            $"VRAM={vramMb}MB → ONNX path, selected={modelId}");
    }

    private static void LogGgufAutoSelection(
        HardwareProfile profile, Internal.Llama.ModelSelectionResult selection)
    {
        const double mb = 1024.0 * 1024.0;
        var freeMb = (profile.GpuInfo.FreeMemoryBytes ?? profile.GpuInfo.TotalMemoryBytes ?? 0) / mb;
        var budgetMb = selection.AvailableVramBytes / mb;
        var marginPct = selection.SafetyMargin * 100;

        System.Diagnostics.Trace.TraceInformation(
            $"[LocalGenerator.auto] Provider={profile.RecommendedProvider}, " +
            $"GPU={profile.GpuInfo.Vendor} {profile.GpuInfo.DeviceName ?? "n/a"}, " +
            $"VRAM raw={freeMb:F0}MB → budget={budgetMb:F0}MB (margin={marginPct:F0}%, " +
            $"KV ctx={selection.BudgetContextLength}) → GGUF path, " +
            $"selected={selection.Selected.AliasName} ({selection.Selected.RepoId}), " +
            $"reason={selection.Reason}");

        // Verbose breakdown of all candidates considered
        foreach (var c in selection.Candidates)
        {
            var weightsMb = c.WeightsBytes / mb;
            var kvMb = c.KvCacheBytes / mb;
            var totalMb = c.TotalBytes / mb;
            System.Diagnostics.Trace.TraceInformation(
                $"[LocalGenerator.auto]   candidate {c.Model.AliasName,-14} " +
                $"weights={weightsMb,7:F0}MB + KV={kvMb,6:F0}MB = {totalMb,7:F0}MB " +
                $"({(c.Fits ? "fits" : "OVER BUDGET")})");
        }

        if (selection.Reason == Internal.Llama.ModelSelectionReason.FallbackToSmallest)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"[LocalGenerator.auto] WARNING: no registered model fits in {budgetMb:F0}MB budget. " +
                $"Selected smallest ({selection.Selected.AliasName}) as fallback — " +
                $"runtime may OOM or partial-offload to CPU. " +
                $"Override with explicit alias (e.g., \"phi-4-mini\") for low-VRAM hosts.");
        }
    }
}
