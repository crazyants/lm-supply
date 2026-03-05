using LMSupply.Pool;

namespace LMSupply.Captioner.Pool;

internal sealed class CaptionerLoader : IModelLoader<ICaptionerModel, CaptionerOptions>
{
    public Task<ICaptionerModel> LoadAsync(string modelId, CaptionerOptions? options, CancellationToken ct)
        => LocalCaptioner.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, CaptionerOptions? options)
        => 500_000_000; // 500 MB
}
