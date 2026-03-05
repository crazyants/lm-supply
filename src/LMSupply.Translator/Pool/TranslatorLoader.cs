using LMSupply.Pool;
using LMSupply.Translator.Models;

namespace LMSupply.Translator.Pool;

internal sealed class TranslatorLoader : IModelLoader<ITranslatorModel, TranslatorOptions>
{
    public Task<ITranslatorModel> LoadAsync(string modelId, TranslatorOptions? options, CancellationToken ct)
        => LocalTranslator.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, TranslatorOptions? options)
    {
        if (TranslatorModelRegistry.Default.TryResolve(modelId, out var info) && info is not null && info.SizeBytes > 0)
            return info.SizeBytes;

        return 500_000_000; // 500 MB default
    }
}
