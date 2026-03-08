using System.Text.Json;
using LMSupply.Generator.Models;

namespace LMSupply.Generator.Internal;

/// <summary>
/// Parses tool call JSON from ONNX model text output.
/// ONNX models output raw text, so tool calls must be detected and parsed from the response.
/// </summary>
/// <remarks>
/// Supports two formats:
/// <list type="number">
/// <item>
/// OpenAI-style tool_calls array:
/// <code>{"tool_calls": [{"id": "call_123", "type": "function", "function": {"name": "fn", "arguments": "{...}"}}]}</code>
/// </item>
/// <item>
/// Direct function call (single tool):
/// <code>{"name": "fn", "arguments": {"key": "value"}}</code>
/// </item>
/// </list>
/// </remarks>
internal static class ToolCallTextParser
{
    /// <summary>
    /// Attempts to parse tool calls from model output text.
    /// Returns null if the text does not contain recognizable tool call JSON.
    /// </summary>
    /// <param name="text">The raw text output from the model.</param>
    /// <returns>A list of parsed tool calls, or null if no tool calls were found.</returns>
    public static IReadOnlyList<ChatToolCall>? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        if (!trimmed.StartsWith('{'))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            // Format 1: {"tool_calls": [...]}
            if (root.TryGetProperty("tool_calls", out var toolCallsElement)
                && toolCallsElement.ValueKind == JsonValueKind.Array)
            {
                return ParseToolCallsArray(toolCallsElement);
            }

            // Format 2: {"name": "fn", "arguments": {...}}
            if (root.TryGetProperty("name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                && root.TryGetProperty("arguments", out var argsElement))
            {
                return ParseDirectFunctionCall(nameElement, argsElement);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<ChatToolCall>? ParseToolCallsArray(JsonElement toolCallsElement)
    {
        var calls = new List<ChatToolCall>();

        foreach (var item in toolCallsElement.EnumerateArray())
        {
            if (!item.TryGetProperty("function", out var fn))
                continue;

            var id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? GenerateCallId()
                : GenerateCallId();

            var name = fn.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String
                ? nm.GetString() ?? string.Empty
                : string.Empty;

            var arguments = fn.TryGetProperty("arguments", out var args)
                ? SerializeArguments(args)
                : "{}";

            if (string.IsNullOrEmpty(name))
                continue;

            calls.Add(new ChatToolCall(id, name, arguments));
        }

        return calls.Count > 0 ? calls : null;
    }

    private static List<ChatToolCall>? ParseDirectFunctionCall(
        JsonElement nameElement, JsonElement argsElement)
    {
        var name = nameElement.GetString();
        if (string.IsNullOrEmpty(name))
            return null;

        var arguments = SerializeArguments(argsElement);

        return [new ChatToolCall(GenerateCallId(), name, arguments)];
    }

    /// <summary>
    /// Serializes arguments to a JSON string.
    /// If the arguments are already a string, returns them directly.
    /// If they are an object/array, serializes to JSON.
    /// </summary>
    private static string SerializeArguments(JsonElement args)
    {
        return args.ValueKind switch
        {
            JsonValueKind.String => args.GetString() ?? "{}",
            JsonValueKind.Object or JsonValueKind.Array => args.GetRawText(),
            _ => "{}"
        };
    }

    private static string GenerateCallId()
    {
        return $"call_{Guid.NewGuid():N}"[..24];
    }
}
