namespace LMSupply.Generator.Models;

/// <summary>
/// Defines a tool that the model can call.
/// Uses OpenAI-compatible format.
/// </summary>
public sealed record ChatToolDefinition
{
    /// <summary>
    /// Tool type. Always "function".
    /// </summary>
    public string Type { get; init; } = "function";

    /// <summary>
    /// Function definition.
    /// </summary>
    public required ChatFunctionDefinition Function { get; init; }
}

/// <summary>
/// Defines a callable function.
/// </summary>
public sealed record ChatFunctionDefinition
{
    /// <summary>
    /// The function name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Description of what the function does.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// JSON Schema for the function parameters.
    /// </summary>
    public string? Parameters { get; init; }
}
