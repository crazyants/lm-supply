namespace LMSupply.Generator.Models;

/// <summary>
/// Represents a tool/function call made by the model.
/// </summary>
/// <param name="Id">Unique identifier for the tool call.</param>
/// <param name="FunctionName">The function name to call.</param>
/// <param name="Arguments">The JSON arguments for the function.</param>
public sealed record ChatToolCall(
    string Id,
    string FunctionName,
    string Arguments);
