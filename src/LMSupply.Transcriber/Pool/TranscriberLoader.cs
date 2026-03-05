using LMSupply.Pool;
using LMSupply.Transcriber.Models;

namespace LMSupply.Transcriber.Pool;

internal sealed class TranscriberLoader : IModelLoader<ITranscriberModel, TranscriberOptions>
{
    public Task<ITranscriberModel> LoadAsync(string modelId, TranscriberOptions? options, CancellationToken ct)
        => LocalTranscriber.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, TranscriberOptions? options)
    {
        if (TranscriberModelRegistry.Default.TryResolve(modelId, out var info) && info is not null && info.SizeBytes > 0)
            return info.SizeBytes;

        return 1_000_000_000; // 1 GB default
    }
}
