using System.Diagnostics;
using LMSupply.Hardware;

namespace LMSupply.Transcriber.Models;

/// <summary>
/// Model registry for the Transcriber domain.
/// </summary>
public sealed class TranscriberModelRegistry : ModelRegistryBase<TranscriberModelInfo>
{
    /// <summary>
    /// Gets the default registry instance with built-in models.
    /// </summary>
    public static TranscriberModelRegistry Default { get; } = new(DefaultModels.All);

    /// <summary>
    /// Initializes a new registry with the specified system models.
    /// </summary>
    /// <param name="systemModels">Models to register as system defaults.</param>
    public TranscriberModelRegistry(IEnumerable<TranscriberModelInfo> systemModels)
        : base(systemModels) { }

    /// <summary>
    /// Gets the optimal model based on current hardware profile.
    /// Uses PerformanceTier to select appropriate model size.
    /// </summary>
    /// <remarks>
    /// Tier mapping:
    /// - Low:    Whisper Tiny (39M params) - ultra-fast
    /// - Medium: Whisper Base (74M params) - balanced
    /// - High:   Whisper Small (244M params) - quality
    /// - Ultra:  Whisper Large V3 Turbo (809M params) - highest quality
    /// </remarks>
    protected override TranscriberModelInfo GetAutoModel()
    {
        var tier = HardwareProfile.Current.Tier;
        Trace.TraceInformation($"[TranscriberModelRegistry] Auto-selecting model for tier: {tier}");

        var model = tier switch
        {
            PerformanceTier.Ultra => DefaultModels.WhisperLargeV3Turbo,
            PerformanceTier.High => DefaultModels.WhisperSmall,
            PerformanceTier.Medium => DefaultModels.WhisperBase,
            _ => DefaultModels.WhisperTiny
        };

        return new TranscriberModelInfo
        {
            Id = model.Id,
            AliasName = "auto",
            DisplayName = model.DisplayName,
            Architecture = model.Architecture,
            ParametersM = model.ParametersM,
            SizeBytes = model.SizeBytes,
            WerLibriSpeech = model.WerLibriSpeech,
            MaxDurationSeconds = model.MaxDurationSeconds,
            SampleRate = model.SampleRate,
            NumMelBins = model.NumMelBins,
            HiddenSize = model.HiddenSize,
            EncoderFile = model.EncoderFile,
            DecoderFile = model.DecoderFile,
            SupportedLanguages = model.SupportedLanguages,
            IsMultilingual = model.IsMultilingual,
            Description = model.Description,
            License = model.License
        };
    }

    /// <summary>
    /// Creates a fallback model info for unknown model IDs (HuggingFace repos or local paths).
    /// </summary>
    protected override TranscriberModelInfo CreateFallbackModelInfo(string modelId)
    {
        Trace.TraceInformation($"[TranscriberModelRegistry] Creating fallback model info for: {modelId}");

        var parts = modelId.Split('/');
        var name = parts.Length > 1 ? parts[1] : modelId;

        return new TranscriberModelInfo
        {
            Id = modelId,
            AliasName = modelId,
            DisplayName = name,
            Architecture = "Whisper"
        };
    }
}
