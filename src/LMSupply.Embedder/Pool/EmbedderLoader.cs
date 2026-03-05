using LMSupply.Pool;

namespace LMSupply.Embedder.Pool;

internal sealed class EmbedderLoader : IModelLoader<IEmbeddingModel, EmbedderOptions>
{
    public Task<IEmbeddingModel> LoadAsync(string modelId, EmbedderOptions? options, CancellationToken ct)
        => LocalEmbedder.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, EmbedderOptions? options)
    {
        // EmbedderModelRegistry.ModelInfo does not expose SizeBytes; use a fixed default.
        return 500_000_000; // 500 MB
    }
}
