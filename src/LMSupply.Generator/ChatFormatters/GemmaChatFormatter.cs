using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;

namespace LMSupply.Generator.ChatFormatters;

/// <summary>
/// Chat formatter for Gemma 2 models.
/// Format: &lt;start_of_turn&gt;user\n{content}&lt;end_of_turn&gt;\n&lt;start_of_turn&gt;model\n
/// Tool results are folded into a user turn with a [tool_result: id] marker
/// because Gemma's chat template does not define a dedicated tool turn.
/// </summary>
public sealed class GemmaChatFormatter : IChatFormatter
{
    private const string StartOfTurn = "<start_of_turn>";
    private const string EndOfTurn = "<end_of_turn>";

    /// <inheritdoc />
    public string FormatName => "gemma";

    /// <inheritdoc />
    public string FormatPrompt(IEnumerable<ChatMessage> messages)
    {
        var sb = new StringBuilder();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.Tool)
            {
                sb.Append(StartOfTurn);
                sb.Append("user\n[tool_result");
                if (!string.IsNullOrEmpty(message.ToolCallId))
                {
                    sb.Append(": ");
                    sb.Append(message.ToolCallId);
                }
                sb.Append("] ");
                sb.Append(message.Content);
                sb.Append(EndOfTurn);
                sb.Append('\n');
                continue;
            }

            var role = message.Role switch
            {
                ChatRole.System => "user", // Gemma treats system as user
                ChatRole.User => "user",
                ChatRole.Assistant => "model",
                _ => throw new ArgumentOutOfRangeException(nameof(messages), message.Role, "Unsupported chat role")
            };

            sb.Append(StartOfTurn);
            sb.Append(role);
            sb.Append('\n');
            sb.Append(message.Role == ChatRole.Assistant
                ? ChatMessageRendering.GetAssistantText(message)
                : message.Content);
            sb.Append(EndOfTurn);
            sb.Append('\n');
        }

        // Add model prompt to start generation
        sb.Append(StartOfTurn);
        sb.Append("model");
        sb.Append('\n');

        return sb.ToString();
    }

    /// <inheritdoc />
    public string GetStopToken() => EndOfTurn;

    /// <inheritdoc />
    public IReadOnlyList<string> GetStopSequences() =>
        [EndOfTurn, StartOfTurn];
}
