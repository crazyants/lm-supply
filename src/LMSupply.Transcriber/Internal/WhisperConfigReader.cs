using System.Diagnostics;
using System.Text.Json;

namespace LMSupply.Transcriber.Internal;

/// <summary>
/// Reads Whisper model configuration from config.json files in model directories.
/// Used to auto-detect parameters (NumMelBins, HiddenSize) for unknown HuggingFace models.
/// </summary>
internal static class WhisperConfigReader
{
    private const string ConfigFileName = "config.json";

    /// <summary>
    /// Reads Whisper-specific parameters from a model directory's config.json.
    /// </summary>
    /// <param name="modelDirectory">Path to the model directory containing config.json.</param>
    /// <returns>Parsed config, or null if config.json doesn't exist or isn't valid Whisper config.</returns>
    public static WhisperModelConfig? ReadConfig(string modelDirectory)
    {
        var configPath = Path.Combine(modelDirectory, ConfigFileName);
        if (!File.Exists(configPath))
            return null;

        try
        {
            var json = File.ReadAllText(configPath);
            return ParseConfig(json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Trace.TraceInformation($"[WhisperConfigReader] Failed to read config from {configPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses Whisper config JSON content.
    /// </summary>
    internal static WhisperModelConfig? ParseConfig(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Verify this is a Whisper model
        if (root.TryGetProperty("model_type", out var modelType))
        {
            var type = modelType.GetString();
            if (type != null && !type.Equals("whisper", StringComparison.OrdinalIgnoreCase))
                return null;
        }

        int? numMelBins = null;
        int? hiddenSize = null;

        if (root.TryGetProperty("num_mel_bins", out var melBins) && melBins.ValueKind == JsonValueKind.Number
            && melBins.TryGetInt32(out var melBinsValue))
            numMelBins = melBinsValue;

        if (root.TryGetProperty("d_model", out var dModel) && dModel.ValueKind == JsonValueKind.Number
            && dModel.TryGetInt32(out var dModelValue))
            hiddenSize = dModelValue;

        // Need at least one useful field
        if (numMelBins == null && hiddenSize == null)
            return null;

        Trace.TraceInformation($"[WhisperConfigReader] Parsed config - NumMelBins: {numMelBins?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}, " +
            $"HiddenSize: {hiddenSize?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}");

        return new WhisperModelConfig
        {
            NumMelBins = numMelBins,
            HiddenSize = hiddenSize
        };
    }
}

/// <summary>
/// Whisper model configuration values extracted from config.json.
/// Null values indicate the field was not present in the config.
/// </summary>
internal sealed class WhisperModelConfig
{
    public int? NumMelBins { get; init; }
    public int? HiddenSize { get; init; }
}
