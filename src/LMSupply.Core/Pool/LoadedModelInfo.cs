namespace LMSupply.Pool;

/// <summary>
/// Information about a loaded model in the pool.
/// </summary>
/// <param name="ModelId">The model identifier.</param>
/// <param name="AllocatedMemoryBytes">Memory allocated for this model.</param>
/// <param name="LoadedAt">When the model was loaded.</param>
/// <param name="LastAccessedAt">When the model was last accessed.</param>
public sealed record LoadedModelInfo(
    string ModelId,
    long AllocatedMemoryBytes,
    DateTime LoadedAt,
    DateTime LastAccessedAt);
