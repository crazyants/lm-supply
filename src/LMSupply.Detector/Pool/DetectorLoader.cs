using LMSupply.Pool;

namespace LMSupply.Detector.Pool;

internal sealed class DetectorLoader : IModelLoader<IDetectorModel, DetectorOptions>
{
    public Task<IDetectorModel> LoadAsync(string modelId, DetectorOptions? options, CancellationToken ct)
        => LocalDetector.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, DetectorOptions? options)
        => 100_000_000; // 100 MB
}
