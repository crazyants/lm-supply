using System.Text.Json;
using LMSupply.Console.Host.Models.OpenAI;

namespace LMSupply.Console.Host.Infrastructure.ToolCalling;

/// <summary>Determines tool_choice behavior.</summary>
public static class ToolCallFormatter
{
    /// <summary>Returns true if tools are active and the model should be allowed to call them.</summary>
    public static bool ShouldEnableTools(JsonElement? toolChoice, IReadOnlyList<Tool>? tools)
    {
        if (tools == null || tools.Count == 0) return false;

        if (toolChoice.HasValue)
        {
            if (toolChoice.Value.ValueKind == JsonValueKind.String)
                return toolChoice.Value.GetString()?.ToLowerInvariant() != "none";
        }

        return true; // default: "auto"
    }
}
