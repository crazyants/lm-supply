using LMSupply.Pool;
using LMSupply.Reranker.Models;

namespace LMSupply.Reranker.Pool;

internal sealed class RerankerLoader : IModelLoader<IRerankerModel, RerankerOptions>
{
    public Task<IRerankerModel> LoadAsync(string modelId, RerankerOptions? options, CancellationToken ct)
        => LocalReranker.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, RerankerOptions? options)
    {
        if (RerankerModelRegistry.Default.TryResolve(modelId, out var info) && info is not null && info.SizeBytes > 0)
            return info.SizeBytes;

        return 500_000_000; // 500 MB default
    }
}
