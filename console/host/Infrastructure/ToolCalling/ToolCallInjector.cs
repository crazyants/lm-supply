using System.Text.Json;
using LMSupply.Console.Host.Models.OpenAI;

namespace LMSupply.Console.Host.Infrastructure.ToolCalling;

/// <summary>Injects tool schema into system prompt so the model can emit tool_calls JSON.</summary>
public static class ToolCallInjector
{
    private const string ToolInstruction = """
        You have access to the following tools. When you need to call a tool, respond ONLY with a valid JSON object in this exact format and nothing else:
        {"tool_calls": [{"id": "call_<unique_id>", "type": "function", "function": {"name": "<tool_name>", "arguments": "<escaped_json_string>"}}]}
        Only call a tool when the user's request requires it. If no tool call is needed, respond normally.

        Available tools:
        """;

    public static string BuildSystemPrompt(string? existingSystem, IReadOnlyList<Tool> tools)
    {
        var toolDefs = tools.Select(t => JsonSerializer.Serialize(new
        {
            name = t.Function.Name,
            description = t.Function.Description,
            parameters = t.Function.Parameters
        }));

        var injection = ToolInstruction + "\n" + string.Join("\n", toolDefs);
        return string.IsNullOrWhiteSpace(existingSystem)
            ? injection
            : existingSystem + "\n\n" + injection;
    }
}
