using System.Globalization;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;

namespace LMSupply.Generator.ChatFormatters;

/// <summary>
/// Chat formatter for Phi-3 and Phi-3.5 models.
/// Format: &lt;|system|&gt;\n{content}&lt;|end|&gt;\n&lt;|user|&gt;\n{content}&lt;|end|&gt;\n&lt;|assistant|&gt;\n
/// Phi has no native tool turn; tool results are folded into a user turn with a
/// [tool_result: id] marker so the model can read back its prior tool call.
/// </summary>
public sealed class Phi3ChatFormatter : IChatFormatter
{
    /// <inheritdoc />
    public string FormatName => "phi3";

    /// <inheritdoc />
    public string FormatPrompt(IEnumerable<ChatMessage> messages)
    {
        var sb = new StringBuilder();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.Tool)
            {
                sb.Append("<|user|>\n[tool_result");
                if (!string.IsNullOrEmpty(message.ToolCallId))
                {
                    sb.Append(": ");
                    sb.Append(message.ToolCallId);
                }
                sb.Append("]\n");
                sb.Append(message.Content);
                sb.Append("<|end|>\n");
                continue;
            }

            var role = message.Role switch
            {
                ChatRole.System => "system",
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                _ => throw new ArgumentOutOfRangeException(nameof(messages), message.Role, "Unsupported chat role")
            };

            sb.Append(CultureInfo.InvariantCulture, $"<|{role}|>\n");
            sb.Append(message.Role == ChatRole.Assistant
                ? ChatMessageRendering.GetAssistantText(message)
                : message.Content);
            sb.Append("<|end|>\n");
        }

        // Add assistant prompt to start generation
        sb.Append("<|assistant|>\n");

        return sb.ToString();
    }

    /// <inheritdoc />
    public string GetStopToken() => "<|end|>";

    /// <inheritdoc />
    public IReadOnlyList<string> GetStopSequences() =>
        ["<|end|>", "<|user|>", "<|system|>", "<|endoftext|>"];
}
