using System.Text.Json;
using LMSupply.Console.Host.Models.OpenAI;

namespace LMSupply.Console.Host.Infrastructure.ToolCalling;

public static class ToolCallParser
{
    /// <summary>
    /// Returns parsed tool calls if the model output is a tool call JSON, otherwise null.
    /// </summary>
    public static IReadOnlyList<ToolCall>? TryParse(string output)
    {
        var trimmed = output.Trim();
        if (!trimmed.StartsWith('{')) return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (!doc.RootElement.TryGetProperty("tool_calls", out var toolCallsEl)) return null;

            var calls = new List<ToolCall>();
            foreach (var item in toolCallsEl.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl)
                    ? idEl.GetString() ?? ApiHelper.GenerateId("call")
                    : ApiHelper.GenerateId("call");

                if (!item.TryGetProperty("function", out var fn)) continue;

                calls.Add(new ToolCall
                {
                    Id = id,
                    Type = "function",
                    Function = new ToolCallFunction
                    {
                        Name = fn.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                        Arguments = fn.TryGetProperty("arguments", out var args) ? args.GetString() ?? "{}" : "{}"
                    }
                });
            }
            return calls.Count > 0 ? calls : null;
        }
        catch
        {
            return null;
        }
    }
}
