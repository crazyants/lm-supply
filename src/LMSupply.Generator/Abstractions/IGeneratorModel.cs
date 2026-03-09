using LMSupply.Generator.Internal.Llama;
using LMSupply.Generator.Models;

namespace LMSupply.Generator.Abstractions;

/// <summary>
/// Represents a loaded text generation model.
/// </summary>
public interface IGeneratorModel : ITextGenerator
{
    /// <summary>
    /// Gets the maximum context length supported by the model.
    /// </summary>
    int MaxContextLength { get; }

    /// <summary>
    /// Gets the chat formatter for this model.
    /// </summary>
    IChatFormatter ChatFormatter { get; }

    /// <summary>
    /// Gets whether GPU acceleration is being used for inference.
    /// </summary>
    bool IsGpuActive { get; }

    /// <summary>
    /// Gets the list of active execution providers.
    /// </summary>
    IReadOnlyList<string> ActiveProviders { get; }

    /// <summary>
    /// Gets the execution provider that was requested.
    /// </summary>
    ExecutionProvider RequestedProvider { get; }

    /// <summary>
    /// Gets the estimated memory usage of this model in bytes.
    /// Uses MemoryEstimator to calculate based on model parameters, context length, and quantization.
    /// </summary>
    long? EstimatedMemoryBytes { get; }

    /// <summary>
    /// Gets information about the model.
    /// </summary>
    GeneratorModelInfo GetModelInfo();

    /// <summary>
    /// Counts the exact number of tokens in the given text using the model's tokenizer.
    /// </summary>
    /// <param name="text">The text to tokenize and count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exact token count.</returns>
    Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the exact number of tokens for formatted chat messages using the model's tokenizer.
    /// Messages are formatted using the model's chat template before tokenization.
    /// </summary>
    /// <param name="messages">The chat messages to format and count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exact token count of the formatted prompt.</returns>
    Task<int> CountTokensAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a generator model.
/// </summary>
/// <param name="ModelId">The model identifier.</param>
/// <param name="ModelPath">The local path to the model files.</param>
/// <param name="MaxContextLength">Maximum context length.</param>
/// <param name="ChatFormat">The chat format name.</param>
/// <param name="ExecutionProvider">The execution provider being used.</param>
public readonly record struct GeneratorModelInfo(
    string ModelId,
    string ModelPath,
    int MaxContextLength,
    string ChatFormat,
    string ExecutionProvider) : IModelInfoBase
{
    /// <summary>
    /// Gets GGUF-specific metadata (only available for GGUF models).
    /// </summary>
    public GgufMetadata? GgufMetadata { get; init; }

    /// <summary>
    /// Gets the backend startup log for diagnostics (llama-server only).
    /// </summary>
    public string? BackendLog { get; init; }

    /// <summary>
    /// Gets the runtime version (e.g., llama-server "b7898").
    /// </summary>
    public string? RuntimeVersion { get; init; }

    /// <summary>
    /// Gets the model identifier (IModelInfoBase.Id).
    /// </summary>
    string IModelInfoBase.Id => ModelId;

    /// <summary>
    /// Gets the model alias name (same as ModelId for Generator).
    /// </summary>
    string IModelInfoBase.AliasName => ModelId;

    /// <summary>
    /// Gets the model description.
    /// </summary>
    string? IModelInfoBase.Description => $"{ChatFormat} model at {ModelPath}";

    /// <summary>
    /// Gets a summary of the model's architecture from GGUF metadata.
    /// </summary>
    public string? GetArchitectureSummary()
    {
        if (GgufMetadata == null)
            return null;

        return GgufMetadata.GetSummary();
    }

    /// <summary>
    /// Gets the estimated parameter count from GGUF metadata.
    /// </summary>
    public long? ParameterCount => GgufMetadata?.EstimatedParameterCount;

    /// <summary>
    /// Gets the quantization type from GGUF metadata.
    /// </summary>
    public string? QuantizationType => GgufMetadata?.QuantizationType;

    /// <summary>
    /// Gets the model architecture from GGUF metadata (e.g., "llama", "gemma").
    /// </summary>
    public string? Architecture => GgufMetadata?.Architecture;
}
