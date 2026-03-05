namespace LMSupply.Console.Host.Infrastructure;

/// <summary>Thrown when a requested model alias/ID cannot be resolved. Mapped to HTTP 404.</summary>
public sealed class ModelResolutionException(string modelId)
    : Exception($"Model '{modelId}' not found. Use GET /v1/models to list available models.")
{
    public string ModelId { get; } = modelId;
}
