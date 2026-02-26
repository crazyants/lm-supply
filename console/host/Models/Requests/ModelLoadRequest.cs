namespace LMSupply.Console.Host.Models.Requests;

/// <summary>
/// Request to pre-load a model into memory with optional configuration.
/// </summary>
public sealed record ModelLoadRequest
{
    /// <summary>
    /// Model type: generator, embedder, reranker, transcriber, synthesizer,
    /// translator, captioner, ocr, detector, segmenter, imagegen
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Model ID or HuggingFace repo ID (e.g., "default", "BAAI/bge-small-en-v1.5")
    /// </summary>
    public string ModelId { get; init; } = "default";

    /// <summary>
    /// Execution provider: Auto, Cuda, DirectML, CoreML, Cpu
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Number of threads for inference (null = ONNX Runtime default)
    /// </summary>
    public int? ThreadCount { get; init; }

    // Generator-specific options

    /// <summary>
    /// Maximum context length for generator models
    /// </summary>
    public int? MaxContextLength { get; init; }

    /// <summary>
    /// Maximum concurrent generation requests
    /// </summary>
    public int? MaxConcurrentRequests { get; init; }

    // Embedder-specific options

    /// <summary>
    /// Maximum sequence length for embedder models
    /// </summary>
    public int? MaxSequenceLength { get; init; }
}
