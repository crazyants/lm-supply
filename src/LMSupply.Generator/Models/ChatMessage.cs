namespace LMSupply.Generator.Models;

/// <summary>
/// Represents a message in a chat conversation.
/// </summary>
/// <param name="Role">The role of the message sender.</param>
/// <param name="Content">The content of the message.</param>
public readonly record struct ChatMessage(ChatRole Role, string Content)
{
    /// <summary>
    /// Tool calls made by the assistant.
    /// </summary>
    public IReadOnlyList<ChatToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// Tool call ID this message is responding to (for Tool role).
    /// </summary>
    public string? ToolCallId { get; init; }

    /// <summary>
    /// Multimodal content parts (text + images). When set, takes precedence over
    /// <see cref="Content"/> for serialization to vision-capable endpoints.
    /// Null for text-only messages (the common case).
    /// </summary>
    public IReadOnlyList<ContentPart>? ContentParts { get; init; }

    /// <summary>
    /// Whether this message contains image content.
    /// </summary>
    public bool IsMultimodal => ContentParts?.Any(p => p is ImageContentPart) == true;

    /// <summary>
    /// Creates a system message.
    /// </summary>
    public static ChatMessage System(string content) => new(ChatRole.System, content);

    /// <summary>
    /// Creates a user message.
    /// </summary>
    public static ChatMessage User(string content) => new(ChatRole.User, content);

    /// <summary>
    /// Creates a user message with an image.
    /// </summary>
    public static ChatMessage UserWithImage(string text, string imageUrl) =>
        new(ChatRole.User, text)
        {
            ContentParts = [new TextContentPart(text), new ImageContentPart { Url = imageUrl }]
        };

    /// <summary>
    /// Creates a user message with multiple content parts (text and images).
    /// </summary>
    public static ChatMessage UserWithContent(IReadOnlyList<ContentPart> parts) =>
        new(ChatRole.User, ExtractTextContent(parts))
        {
            ContentParts = parts
        };

    /// <summary>
    /// Creates an assistant message.
    /// </summary>
    public static ChatMessage Assistant(string content) => new(ChatRole.Assistant, content);

    /// <summary>
    /// Creates an assistant message with tool calls.
    /// </summary>
    public static ChatMessage AssistantToolCalls(IReadOnlyList<ChatToolCall> toolCalls)
        => new(ChatRole.Assistant, string.Empty) { ToolCalls = toolCalls };

    /// <summary>
    /// Creates a tool result message.
    /// </summary>
    public static ChatMessage ToolResult(string toolCallId, string content)
        => new(ChatRole.Tool, content) { ToolCallId = toolCallId };

    /// <summary>
    /// Extracts concatenated text from content parts (joined by space).
    /// Used as a fallback Content string for formatters that can't render images.
    /// </summary>
    private static string ExtractTextContent(IReadOnlyList<ContentPart> parts) =>
        string.Join(" ", parts.OfType<TextContentPart>().Select(p => p.Text));
}

/// <summary>
/// Represents the role of a participant in a chat conversation.
/// </summary>
public enum ChatRole
{
    /// <summary>
    /// System message providing context or instructions.
    /// </summary>
    System,

    /// <summary>
    /// User message from the human participant.
    /// </summary>
    User,

    /// <summary>
    /// Assistant message from the AI model.
    /// </summary>
    Assistant,

    /// <summary>
    /// Tool result message containing the output of a tool call.
    /// </summary>
    Tool
}
