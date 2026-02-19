using LMSupply.Download;

namespace LMSupply.Core.Download;

/// <summary>
/// Provides centralized model path resolution for all domain packages.
/// Handles local paths, HuggingFace downloads, and subfolder discovery.
/// </summary>
public sealed class ModelPathResolver : IDisposable
{
    private readonly HuggingFaceDownloader _downloader;
    private bool _disposed;

    /// <summary>
    /// Creates a new model path resolver with the specified cache directory.
    /// </summary>
    /// <param name="cacheDirectory">The directory to cache downloaded models.</param>
    public ModelPathResolver(string? cacheDirectory = null)
    {
        cacheDirectory ??= CacheManager.GetDefaultCacheDirectory();
        _downloader = new HuggingFaceDownloader(cacheDirectory);
    }

    /// <summary>
    /// Result of model path resolution.
    /// </summary>
    public sealed class ResolveResult
    {
        /// <summary>
        /// The resolved path to the model file.
        /// </summary>
        public required string ModelPath { get; init; }

        /// <summary>
        /// The base model directory (for tokenizer/config files).
        /// </summary>
        public required string BaseDirectory { get; init; }

        /// <summary>
        /// The directory containing ONNX model files (may differ from BaseDirectory for subfolder repos).
        /// </summary>
        public required string OnnxDirectory { get; init; }

        /// <summary>
        /// Discovery result if downloaded from HuggingFace (null for local paths).
        /// </summary>
        public ModelDiscoveryResult? Discovery { get; init; }
    }

    /// <summary>
    /// Resolves a single ONNX model file path from a model ID or local path.
    /// </summary>
    /// <param name="modelIdOrPath">Model ID (HuggingFace) or local path.</param>
    /// <param name="expectedOnnxFile">Expected ONNX filename (e.g., "model.onnx").</param>
    /// <param name="preferences">Download preferences for HuggingFace models.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolution result containing paths and discovery info.</returns>
    public async Task<ResolveResult> ResolveModelAsync(
        string modelIdOrPath,
        string expectedOnnxFile = "model.onnx",
        ModelPreferences? preferences = null,
        CancellationToken cancellationToken = default)
    {
        // Check if it's a local file path
        if (File.Exists(modelIdOrPath))
        {
            var directory = Path.GetDirectoryName(modelIdOrPath) ?? modelIdOrPath;
            return new ResolveResult
            {
                ModelPath = modelIdOrPath,
                BaseDirectory = directory,
                OnnxDirectory = directory,
                Discovery = null
            };
        }

        // Check if it's a local directory path
        if (Directory.Exists(modelIdOrPath))
        {
            var localPath = Path.Combine(modelIdOrPath, expectedOnnxFile);
            if (File.Exists(localPath))
            {
                return new ResolveResult
                {
                    ModelPath = localPath,
                    BaseDirectory = modelIdOrPath,
                    OnnxDirectory = modelIdOrPath,
                    Discovery = null
                };
            }

            // Try to find any ONNX file in the directory
            var onnxFiles = Directory.GetFiles(modelIdOrPath, "*.onnx", SearchOption.AllDirectories);
            if (onnxFiles.Length > 0)
            {
                var firstOnnx = onnxFiles[0];
                var onnxDir = Path.GetDirectoryName(firstOnnx) ?? modelIdOrPath;
                return new ResolveResult
                {
                    ModelPath = firstOnnx,
                    BaseDirectory = modelIdOrPath,
                    OnnxDirectory = onnxDir,
                    Discovery = null
                };
            }

            throw new FileNotFoundException(
                $"No ONNX model found in directory: {modelIdOrPath}",
                expectedOnnxFile);
        }

        // Download from HuggingFace using discovery for proper subfolder handling
        preferences ??= ModelPreferences.Default;

        var (modelDir, discovery) = await _downloader.DownloadWithDiscoveryAsync(
            modelIdOrPath,
            preferences: preferences,
            cancellationToken: cancellationToken);

        // Use discovery result to find ONNX file in correct directory
        var onnxDirectory = discovery.GetOnnxDirectory(modelDir);
        var modelPath = Path.Combine(onnxDirectory, expectedOnnxFile);

        // If specified file not found, try to use discovered ONNX files
        if (!File.Exists(modelPath) && discovery.OnnxFiles.Count > 0)
        {
            // Use first discovered ONNX file
            modelPath = ModelDiscoveryResult.GetFilePath(modelDir, discovery.OnnxFiles[0]);
        }

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                $"ONNX model not found in {modelDir}. Expected: {expectedOnnxFile}. " +
                $"Discovered ONNX files: [{string.Join(", ", discovery.OnnxFiles)}]",
                modelPath);
        }

        return new ResolveResult
        {
            ModelPath = modelPath,
            BaseDirectory = modelDir,
            OnnxDirectory = onnxDirectory,
            Discovery = discovery
        };
    }

    /// <summary>
    /// Resolves encoder-decoder model paths from a model ID or local path.
    /// </summary>
    /// <param name="modelIdOrPath">Model ID (HuggingFace) or local path.</param>
    /// <param name="expectedEncoderFile">Expected encoder filename.</param>
    /// <param name="expectedDecoderFile">Expected decoder filename.</param>
    /// <param name="preferences">Download preferences for HuggingFace models.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolution result with encoder/decoder paths.</returns>
    public async Task<EncoderDecoderResolveResult> ResolveEncoderDecoderAsync(
        string modelIdOrPath,
        string expectedEncoderFile = "encoder_model.onnx",
        string expectedDecoderFile = "decoder_model_merged.onnx",
        ModelPreferences? preferences = null,
        CancellationToken cancellationToken = default)
    {
        // Check if it's a local directory path
        if (Directory.Exists(modelIdOrPath))
        {
            var encoderPath = Path.Combine(modelIdOrPath, expectedEncoderFile);
            var decoderPath = Path.Combine(modelIdOrPath, expectedDecoderFile);

            if (File.Exists(encoderPath) && File.Exists(decoderPath))
            {
                return new EncoderDecoderResolveResult
                {
                    EncoderPath = encoderPath,
                    DecoderPath = decoderPath,
                    BaseDirectory = modelIdOrPath,
                    OnnxDirectory = modelIdOrPath,
                    Discovery = null
                };
            }
        }

        // Download from HuggingFace using discovery
        preferences ??= ModelPreferences.Default;

        var (modelDir, discovery) = await _downloader.DownloadWithDiscoveryAsync(
            modelIdOrPath,
            preferences: preferences,
            cancellationToken: cancellationToken);

        // Use discovery result for path resolution
        var onnxDirectory = discovery.GetOnnxDirectory(modelDir);

        // Try discovery-based paths first
        var encoderPath2 = discovery.GetEncoderPath(modelDir)
            ?? Path.Combine(onnxDirectory, expectedEncoderFile);
        var decoderPath2 = discovery.GetDecoderPath(modelDir)
            ?? Path.Combine(onnxDirectory, expectedDecoderFile);

        if (!File.Exists(encoderPath2))
        {
            throw new FileNotFoundException(
                $"Encoder model not found. Expected: {expectedEncoderFile}. " +
                $"Discovered encoder files: [{string.Join(", ", discovery.EncoderFiles)}]",
                encoderPath2);
        }

        if (!File.Exists(decoderPath2))
        {
            throw new FileNotFoundException(
                $"Decoder model not found. Expected: {expectedDecoderFile}. " +
                $"Discovered decoder files: [{string.Join(", ", discovery.DecoderFiles)}]",
                decoderPath2);
        }

        return new EncoderDecoderResolveResult
        {
            EncoderPath = encoderPath2,
            DecoderPath = decoderPath2,
            BaseDirectory = modelDir,
            OnnxDirectory = onnxDirectory,
            Discovery = discovery
        };
    }

    /// <summary>
    /// Result of encoder-decoder model path resolution.
    /// </summary>
    public sealed class EncoderDecoderResolveResult
    {
        /// <summary>
        /// Path to the encoder model file.
        /// </summary>
        public required string EncoderPath { get; init; }

        /// <summary>
        /// Path to the decoder model file.
        /// </summary>
        public required string DecoderPath { get; init; }

        /// <summary>
        /// The base model directory (for tokenizer/config files).
        /// </summary>
        public required string BaseDirectory { get; init; }

        /// <summary>
        /// The directory containing ONNX model files.
        /// </summary>
        public required string OnnxDirectory { get; init; }

        /// <summary>
        /// Discovery result if downloaded from HuggingFace.
        /// </summary>
        public ModelDiscoveryResult? Discovery { get; init; }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _downloader.Dispose();
    }
}
