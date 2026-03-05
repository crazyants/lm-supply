using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Internal;
using LMSupply.Pool;

namespace LMSupply.Generator;

/// <summary>
/// Pool for managing multiple generator model instances with memory protection.
/// Delegates to the generic ModelPool infrastructure.
/// </summary>
public sealed class GeneratorPool : IAsyncDisposable
{
    private readonly ModelPool<IGeneratorModel, GeneratorOptions> _inner;

    /// <summary>
    /// Creates a new generator pool with the specified options.
    /// </summary>
    public GeneratorPool(IGeneratorModelFactory factory, GeneratorPoolOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var poolOptions = options != null
            ? new ModelPoolOptions
            {
                MaxMemoryBytes = options.MaxMemoryBytes,
                MemorySafetyMargin = options.MemorySafetyMargin,
                MaxLoadedModels = options.MaxLoadedModels,
            }
            : null;
        _inner = new ModelPool<IGeneratorModel, GeneratorOptions>(new GeneratorLoader(factory), poolOptions);
    }

    /// <summary>Gets the number of loaded models.</summary>
    public int LoadedModelCount => _inner.LoadedModelCount;

    /// <summary>Gets the total allocated memory across all loaded models.</summary>
    public long AllocatedMemoryBytes => _inner.AllocatedMemoryBytes;

    /// <summary>Gets the available memory for model loading.</summary>
    public long AvailableMemoryBytes => _inner.AvailableMemoryBytes;

    /// <summary>Gets or loads a generator model by ID.</summary>
    public Task<IGeneratorModel> GetOrLoadAsync(
        string modelId,
        GeneratorOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.GetOrLoadAsync(modelId, options, cancellationToken);

    /// <summary>Checks if a model is currently loaded.</summary>
    public bool IsLoaded(string modelId) => _inner.IsLoaded(modelId);

    /// <summary>Unloads a specific model.</summary>
    public Task UnloadAsync(string modelId, CancellationToken cancellationToken = default)
        => _inner.UnloadAsync(modelId, cancellationToken);

    /// <summary>Unloads all models.</summary>
    public Task UnloadAllAsync(CancellationToken cancellationToken = default)
        => _inner.UnloadAllAsync(cancellationToken);

    /// <summary>Gets information about all loaded models.</summary>
    public IReadOnlyList<LoadedModelInfo> GetLoadedModels() => _inner.GetLoadedModels();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>Configuration options for the generator pool.</summary>
public sealed class GeneratorPoolOptions
{
    /// <summary>Maximum memory to allocate for models (in bytes).</summary>
    public long? MaxMemoryBytes { get; set; }

    /// <summary>Safety margin for memory calculations (0.0-1.0). Defaults to 0.2.</summary>
    public double MemorySafetyMargin { get; set; } = 0.2;

    /// <summary>Maximum number of models to keep loaded simultaneously. Defaults to 2.</summary>
    public int MaxLoadedModels { get; set; } = 2;
}
