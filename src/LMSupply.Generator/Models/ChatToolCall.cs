namespace LMSupply.Generator.Models;

/// <summary>
/// Represents a tool/function call made by the model.
/// </summary>
public sealed record ChatToolCall
{
    /// <summary>
    /// Unique identifier for the tool call.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The function name to call.
    /// </summary>
    public required string FunctionName { get; init; }

    /// <summary>
    /// The JSON arguments for the function.
    /// </summary>
    public required string Arguments { get; init; }
}
