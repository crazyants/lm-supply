using LMSupply.Core.Download;
using LMSupply.Download;
using LMSupply.Reranker.Inference;
using LMSupply.Reranker.Models;
using LMSupply.Reranker.Utils;

namespace LMSupply.Reranker;

/// <summary>
/// Main entry point for loading and using reranker models.
/// </summary>
public static class LocalReranker
{
    /// <summary>
    /// Default model to use when no model is specified.
    /// MS-MARCO MiniLM L-6 v2, 22M params, high quality cross-encoder.
    /// </summary>
    public const string DefaultModel = "default";

    /// <summary>
    /// Loads the default reranker model.
    /// </summary>
    /// <param name="options">Optional configuration options.</param>
    /// <param name="progress">Optional progress reporting for downloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A loaded reranker ready for inference.</returns>
    public static Task<IRerankerModel> LoadDefaultAsync(
        RerankerOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return LoadAsync(DefaultModel, options, progress, cancellationToken);
    }

    /// <summary>
    /// Loads a reranker model by name or path.
    /// </summary>
    /// <param name="modelIdOrPath">
    /// Either a model alias (e.g., "default", "quality", "fast"),
    /// a HuggingFace model ID (e.g., "cross-encoder/ms-marco-MiniLM-L-6-v2"),
    /// a local path to an ONNX model file,
    /// or a GGUF model (prefix with "gguf:" or use repo ending in "-GGUF").
    /// <para>
    /// <b>GGUF compatibility:</b> Only traditional cross-encoder models are supported
    /// (e.g., BAAI/bge-reranker-v2-m3-GGUF, jinaai/jina-reranker-v1-turbo-en-GGUF).
    /// Generative rerankers (e.g., Qwen3-Reranker) that require prompt-based "yes/no"
    /// scoring are NOT compatible with llama-server's --pooling rank mode and will
    /// produce near-zero garbage scores.
    /// </para>
    /// </param>
    /// <param name="options">Optional configuration options.</param>
    /// <param name="progress">Optional progress reporting for downloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A loaded reranker ready for inference.</returns>
    public static async Task<IRerankerModel> LoadAsync(
        string modelIdOrPath,
        RerankerOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new RerankerOptions();
        options.ModelId = modelIdOrPath;

        // Check for GGUF format
        if (IsGgufModel(modelIdOrPath))
        {
            return await LoadGgufAsync(modelIdOrPath, options, progress, cancellationToken);
        }

        var reranker = new Reranker(options);

        // Eagerly initialize and warm up the model
        await reranker.WarmupAsync(cancellationToken);

        return reranker;
    }

    /// <summary>
    /// Checks if the model identifier refers to a GGUF model.
    /// </summary>
    private static bool IsGgufModel(string modelIdOrPath)
    {
        // Check for "gguf:" prefix
        if (modelIdOrPath.StartsWith("gguf:", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check for .gguf extension
        if (modelIdOrPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if local file exists and has .gguf extension
        if (File.Exists(modelIdOrPath) &&
            modelIdOrPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check for GGUF indicators in HuggingFace repo name
        var lowerPath = modelIdOrPath.ToLowerInvariant();
        if (lowerPath.Contains("-gguf") || lowerPath.Contains("_gguf"))
            return true;

        return false;
    }

    /// <summary>
    /// Loads a GGUF reranker model.
    /// </summary>
    private static async Task<IRerankerModel> LoadGgufAsync(
        string modelIdOrPath,
        RerankerOptions options,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        string modelPath;
        string modelId;

        // Remove gguf: prefix if present
        var cleanPath = modelIdOrPath.StartsWith("gguf:", StringComparison.OrdinalIgnoreCase)
            ? modelIdOrPath[5..]
            : modelIdOrPath;

        // Check if it's a local file
        if (File.Exists(cleanPath))
        {
            modelPath = cleanPath;
            modelId = Path.GetFileNameWithoutExtension(modelPath);
        }
        // Check if it's a HuggingFace repo ID
        else if (cleanPath.Contains('/'))
        {
            var cacheDir = options.CacheDirectory ?? CacheManager.GetDefaultCacheDirectory();

            // Download the GGUF file from HuggingFace
            using var downloader = new GgufDownloader(cacheDir);
            modelPath = await downloader.DownloadAsync(
                cleanPath,
                preferredQuantization: "Q4_K_M",
                progress: progress,
                cancellationToken: cancellationToken);

            modelId = cleanPath.Split('/').Last();
        }
        else
        {
            throw new ModelNotFoundException(
                $"GGUF reranker model not found: '{modelIdOrPath}'. " +
                "Provide a local path to a .gguf file or a HuggingFace repo ID " +
                "(e.g., 'gguf:BAAI/bge-reranker-v2-m3-GGUF').",
                modelIdOrPath);
        }

        return await LlamaServerRerankerModel.LoadAsync(
            modelId,
            modelPath,
            options,
            progress,
            cancellationToken);
    }

    /// <summary>
    /// Gets a list of pre-configured model aliases available for use.
    /// </summary>
    /// <returns>Available model aliases.</returns>
    public static IEnumerable<string> GetAvailableModels()
    {
        return ModelRegistry.Default.GetAliases();
    }

    /// <summary>
    /// Gets all registered model information.
    /// </summary>
    /// <returns>Collection of model information.</returns>
    public static IEnumerable<ModelInfo> GetAllModels()
    {
        return ModelRegistry.Default.GetAll();
    }
}
