using System.Text.Json;
using LMSupply.Console.Host.Models.OpenAI;

namespace LMSupply.Console.Host.Infrastructure;

public static class ResponseFormatHelper
{
    /// <summary>Builds additional system prompt text for JSON mode/schema enforcement.</summary>
    public static string? BuildJsonInstruction(ResponseFormat? format)
    {
        if (format == null || format.Type == "text") return null;

        if (format.Type == "json_object")
            return "You must respond with valid JSON only. No markdown, no explanation — only the raw JSON object.";

        if (format.Type == "json_schema" && format.JsonSchema?.Schema.HasValue == true)
        {
            var schema = JsonSerializer.Serialize(format.JsonSchema.Schema.Value);
            return $"You must respond with valid JSON that exactly conforms to this JSON schema: {schema}. No markdown, no explanation — only the raw JSON.";
        }

        return null;
    }

    /// <summary>Validates JSON output. Returns true if valid or no strict schema required.</summary>
    public static bool ValidateJson(string output, ResponseFormat? format)
    {
        if (format?.Type is not "json_schema") return true;
        if (format.JsonSchema?.Strict != true) return true;

        try { JsonDocument.Parse(output.Trim()); return true; }
        catch { return false; }
    }
}
