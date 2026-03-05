using LMSupply.Pool;

namespace LMSupply.ImageGenerator.Pool;

internal sealed class ImageGeneratorLoader : IModelLoader<IImageGeneratorModel, ImageGeneratorOptions>
{
    public Task<IImageGeneratorModel> LoadAsync(string modelId, ImageGeneratorOptions? options, CancellationToken ct)
        => LocalImageGenerator.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, ImageGeneratorOptions? options)
        => 4_000_000_000; // 4 GB
}
