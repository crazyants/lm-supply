using System.Text.Json;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;

namespace LMSupply.Generator.ChatFormatters;

/// <summary>
/// Chat formatter for Mistral/Mixtral models.
/// Format: [INST] {user_message} [/INST] {assistant_message}
/// Tool calls and results use Mistral v3 instruct tokens
/// ([TOOL_CALLS], [TOOL_RESULTS], [/TOOL_RESULTS]).
/// </summary>
public sealed class MistralChatFormatter : IChatFormatter
{
    private const string InstStart = "[INST]";
    private const string InstEnd = "[/INST]";
    private const string ToolCallsTag = "[TOOL_CALLS]";
    private const string ToolResultsStart = "[TOOL_RESULTS]";
    private const string ToolResultsEnd = "[/TOOL_RESULTS]";
    private const string BosToken = "<s>";
    private const string EosToken = "</s>";

    /// <inheritdoc />
    public string FormatName => "mistral";

    /// <inheritdoc />
    public string FormatPrompt(IEnumerable<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        var messagesList = messages.ToList();
        string? systemMessage = null;

        // Extract system message if present
        var systemMsgIndex = messagesList.FindIndex(m => m.Role == ChatRole.System);
        if (systemMsgIndex >= 0)
        {
            systemMessage = messagesList[systemMsgIndex].Content;
            messagesList.RemoveAt(systemMsgIndex);
        }

        sb.Append(BosToken);

        for (var i = 0; i < messagesList.Count; i++)
        {
            var message = messagesList[i];

            if (message.Role == ChatRole.User)
            {
                sb.Append(InstStart);
                sb.Append(' ');

                // Include system message with first user message
                if (systemMessage != null && i == 0)
                {
                    sb.Append(systemMessage);
                    sb.Append("\n\n");
                }

                sb.Append(message.Content);
                sb.Append(' ');
                sb.Append(InstEnd);
            }
            else if (message.Role == ChatRole.Assistant)
            {
                if (message.ToolCalls is { Count: > 0 } && string.IsNullOrEmpty(message.Content))
                {
                    sb.Append(ToolCallsTag);
                    sb.Append(' ');
                    sb.Append(SerializeToolCallsForMistral(message.ToolCalls));
                    sb.Append(EosToken);
                }
                else
                {
                    sb.Append(' ');
                    sb.Append(message.Content);
                    sb.Append(EosToken);
                }
            }
            else if (message.Role == ChatRole.Tool)
            {
                sb.Append(ToolResultsStart);
                sb.Append(' ');
                sb.Append(SerializeToolResult(message.ToolCallId, message.Content));
                sb.Append(' ');
                sb.Append(ToolResultsEnd);
            }
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public string GetStopToken() => EosToken;

    /// <inheritdoc />
    public IReadOnlyList<string> GetStopSequences() =>
        [EosToken, InstStart];

    private static string SerializeToolCallsForMistral(IReadOnlyList<ChatToolCall> toolCalls)
    {
        // Mistral v3 emits [{ "name": ..., "arguments": ..., "id": ... }] inside [TOOL_CALLS]
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var call in toolCalls)
            {
                writer.WriteStartObject();
                writer.WriteString("name", call.FunctionName);
                writer.WriteString("arguments", call.Arguments);
                writer.WriteString("id", call.Id);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string SerializeToolResult(string? callId, string content)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("call_id", callId ?? string.Empty);
            writer.WriteString("content", content);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
