namespace LMSupply.Generator.Models;

/// <summary>
/// Result of a chat completion that may contain text and/or tool calls.
/// </summary>
public sealed record ChatCompletionResult
{
    /// <summary>
    /// Text content of the response (may be null if only tool calls).
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Tool calls made by the model (may be null if only text).
    /// </summary>
    public IReadOnlyList<ChatToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// Whether the model made tool calls.
    /// </summary>
    public bool HasToolCalls => ToolCalls is { Count: > 0 };

    /// <summary>
    /// Finish reason from the model.
    /// </summary>
    public string? FinishReason { get; init; }
}
