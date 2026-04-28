using System.Text.Json;
using LMSupply.Generator.Models;

namespace LMSupply.Generator.ChatFormatters;

/// <summary>
/// Shared helpers for rendering <see cref="ChatMessage"/> content into prompt text
/// across formatter templates. Centralizes assistant-turn fallback so messages
/// produced via <see cref="ChatMessage.AssistantToolCalls"/> (empty Content + populated
/// ToolCalls) do not collapse into an empty turn — the model would otherwise lose
/// visibility of its prior tool call across turns.
/// </summary>
internal static class ChatMessageRendering
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false
    };

    /// <summary>
    /// Returns the textual content for an assistant turn, falling back to a JSON
    /// snapshot of <see cref="ChatMessage.ToolCalls"/> when the message was created
    /// via <see cref="ChatMessage.AssistantToolCalls"/> (empty Content).
    /// </summary>
    public static string GetAssistantText(ChatMessage message)
    {
        if (!string.IsNullOrEmpty(message.Content))
            return message.Content;

        if (message.ToolCalls is null || message.ToolCalls.Count == 0)
            return message.Content;

        return SerializeToolCalls(message.ToolCalls);
    }

    /// <summary>
    /// Serializes tool calls in OpenAI-compatible shape so a downstream model can
    /// parse them back: <c>{"tool_calls":[{"id":"...","type":"function",
    /// "function":{"name":"...","arguments":"..."}}]}</c>.
    /// </summary>
    public static string SerializeToolCalls(IReadOnlyList<ChatToolCall> toolCalls)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("tool_calls");
            foreach (var call in toolCalls)
            {
                writer.WriteStartObject();
                writer.WriteString("id", call.Id);
                writer.WriteString("type", "function");
                writer.WriteStartObject("function");
                writer.WriteString("name", call.FunctionName);
                writer.WriteString("arguments", call.Arguments);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
