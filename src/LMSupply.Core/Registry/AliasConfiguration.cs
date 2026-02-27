using System.Text.Json;

namespace LMSupply;

/// <summary>
/// Provides loading and parsing of user alias configurations from JSON files.
/// </summary>
public static class AliasConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Default alias configuration file path: ~/.lmsupply/aliases.json
    /// </summary>
    public static string DefaultFilePath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lmsupply",
            "aliases.json");

    /// <summary>
    /// Parses a JSON string containing domain-scoped alias definitions.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> ParseAliasJson(string json)
    {
        var doc = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            json, JsonOptions);
        return doc ?? [];
    }

    /// <summary>
    /// Loads alias definitions from a JSON file. Returns empty if the file doesn't exist.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return [];

        var json = File.ReadAllText(filePath);
        return ParseAliasJson(json);
    }

    /// <summary>
    /// Loads alias definitions from the default file path (~/.lmsupply/aliases.json).
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> LoadFromDefault()
        => LoadFromFile(DefaultFilePath);

    /// <summary>
    /// Applies a set of parsed aliases to a registry for a specific domain.
    /// </summary>
    public static void ApplyToRegistry<TModelInfo>(
        IModelRegistry<TModelInfo> registry,
        Dictionary<string, string> aliases)
        where TModelInfo : IModelInfoBase
    {
        foreach (var (alias, target) in aliases)
        {
            registry.RegisterAlias(alias, target);
        }
    }
}
