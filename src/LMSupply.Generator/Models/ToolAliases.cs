namespace LMSupply.Generator.Models;

/// <summary>
/// Alias for <see cref="ChatToolDefinition"/> for convenience.
/// </summary>
public sealed record ToolDefinition(
    string Name,
    string? Description = null,
    System.Text.Json.JsonElement? Parameters = null)
{
    /// <summary>
    /// Converts to <see cref="ChatToolDefinition"/>.
    /// </summary>
    public static implicit operator ChatToolDefinition(ToolDefinition td)
        => new(td.Name, td.Description, td.Parameters);

    /// <summary>
    /// Converts from <see cref="ChatToolDefinition"/>.
    /// </summary>
    public static implicit operator ToolDefinition(ChatToolDefinition td)
        => new(td.Name, td.Description, td.Parameters);
}

/// <summary>
/// Alias for <see cref="ChatToolCall"/> for convenience.
/// </summary>
public sealed record ToolCall(
    string Id,
    string FunctionName,
    string Arguments)
{
    /// <summary>
    /// Converts to <see cref="ChatToolCall"/>.
    /// </summary>
    public static implicit operator ChatToolCall(ToolCall tc)
        => new(tc.Id, tc.FunctionName, tc.Arguments);

    /// <summary>
    /// Converts from <see cref="ChatToolCall"/>.
    /// </summary>
    public static implicit operator ToolCall(ChatToolCall tc)
        => new(tc.Id, tc.FunctionName, tc.Arguments);
}
