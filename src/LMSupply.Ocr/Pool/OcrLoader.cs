using LMSupply.Pool;

namespace LMSupply.Ocr.Pool;

internal sealed class OcrLoader : IModelLoader<IOcr, OcrOptions>
{
    public Task<IOcr> LoadAsync(string modelId, OcrOptions? options, CancellationToken ct)
        => LocalOcr.LoadAsync(modelId, null, options, null, ct);

    public long EstimateMemoryBytes(string modelId, OcrOptions? options)
        => 100_000_000; // 100 MB
}
