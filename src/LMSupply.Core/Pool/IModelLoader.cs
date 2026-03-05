namespace LMSupply.Pool;

/// <summary>
/// Defines how a domain loads and estimates memory for a model.
/// Each domain implements this interface internally.
/// </summary>
/// <typeparam name="TModel">The model type (must implement IAsyncDisposable).</typeparam>
/// <typeparam name="TOptions">The options type for loading.</typeparam>
public interface IModelLoader<TModel, TOptions>
    where TModel : IAsyncDisposable
    where TOptions : class
{
    /// <summary>
    /// Loads a model by ID with the specified options.
    /// </summary>
    Task<TModel> LoadAsync(string modelId, TOptions? options, CancellationToken cancellationToken);

    /// <summary>
    /// Estimates the memory requirement in bytes for loading this model.
    /// Return a reasonable domain-specific default if unknown.
    /// </summary>
    long EstimateMemoryBytes(string modelId, TOptions? options);
}
