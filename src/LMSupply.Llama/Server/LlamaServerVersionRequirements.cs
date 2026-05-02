using System.Text.RegularExpressions;

namespace LMSupply.Llama.Server;

/// <summary>
/// Validates that the installed llama-server version meets minimum requirements
/// for specific model architectures.
/// </summary>
public static partial class LlamaServerVersionRequirements
{
    /// <summary>
    /// Minimum llama-server build number required by model architecture.
    /// Key: chat format name (matches IChatFormatter.FormatName).
    /// Value: minimum build number (e.g., 8672 for "b8672").
    /// </summary>
    private static readonly Dictionary<string, int> MinBuildByFormat = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gemma4"]       = 8672,   // Gemma 4 native support (GGUF metadata auto-detect)
        ["spec-ngram"]   = 8500,   // --spec-type ngram support
        ["kv-q8-vulkan"] = 8500,   // KV cache Q8_0 stable on Vulkan
    };

    [GeneratedRegex(@"^b(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex BuildNumberPattern();

    /// <summary>
    /// Parses the build number from a llama-server version tag (e.g., "b8672" → 8672).
    /// Returns null if the version string doesn't match the expected pattern.
    /// </summary>
    public static int? ParseBuildNumber(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var match = BuildNumberPattern().Match(version);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var build))
            return build;

        return null;
    }

    /// <summary>
    /// Gets the minimum required build number for a given chat format.
    /// Returns null if no minimum requirement exists.
    /// </summary>
    public static int? GetMinimumBuild(string chatFormatName)
    {
        if (MinBuildByFormat.TryGetValue(chatFormatName, out var minBuild))
            return minBuild;
        return null;
    }

    /// <summary>
    /// Validates that the server version meets the minimum requirement for the model.
    /// Throws <see cref="InvalidOperationException"/> if the version is too old.
    /// Silently passes if the version cannot be parsed (graceful degradation).
    /// </summary>
    public static void Validate(string? serverVersion, string chatFormatName)
    {
        var minBuild = GetMinimumBuild(chatFormatName);
        if (minBuild == null)
            return; // No requirement for this format

        var actualBuild = ParseBuildNumber(serverVersion);
        if (actualBuild == null)
            return; // Can't determine version — allow to proceed (graceful)

        if (actualBuild.Value < minBuild.Value)
        {
            throw new InvalidOperationException(
                $"llama-server {serverVersion} is too old for {chatFormatName} models. " +
                $"Minimum required: b{minBuild}. " +
                $"Delete the cached llama-server to trigger a fresh download of the latest version.");
        }
    }
}
