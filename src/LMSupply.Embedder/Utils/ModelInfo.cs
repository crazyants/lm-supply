namespace LMSupply.Embedder.Utils;

/// <summary>
/// Configuration information for a pre-configured embedding model.
/// </summary>
public sealed record ModelInfo : IModelInfoBase
{
    /// <summary>
    /// Gets the unique identifier for this model (HuggingFace repository ID).
    /// </summary>
    public required string RepoId { get; init; }

    /// <summary>
    /// Gets the user-friendly alias name for this model (e.g., "default", "fast").
    /// Set internally by the registry when resolving.
    /// </summary>
    public string AliasName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the embedding dimensions.
    /// </summary>
    public required int Dimensions { get; init; }

    /// <summary>
    /// Gets the maximum input sequence length.
    /// </summary>
    public required int MaxSequenceLength { get; init; }

    /// <summary>
    /// Gets the pooling mode for generating embeddings.
    /// </summary>
    public required PoolingMode PoolingMode { get; init; }

    /// <summary>
    /// Gets whether to lowercase input text.
    /// </summary>
    public required bool DoLowerCase { get; init; }

    /// <summary>
    /// Gets the model description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the subfolder within the HuggingFace repository.
    /// </summary>
    public string? Subfolder { get; init; }

    // IModelInfoBase implementation
    string IModelInfoBase.Id => RepoId;
    int? IModelInfoBase.ContextLength => MaxSequenceLength;
}
