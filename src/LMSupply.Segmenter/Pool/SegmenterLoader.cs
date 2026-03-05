using LMSupply.Pool;

namespace LMSupply.Segmenter.Pool;

internal sealed class SegmenterLoader : IModelLoader<ISegmenterModel, SegmenterOptions>
{
    public Task<ISegmenterModel> LoadAsync(string modelId, SegmenterOptions? options, CancellationToken ct)
        => LocalSegmenter.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, SegmenterOptions? options)
        => 200_000_000; // 200 MB
}
