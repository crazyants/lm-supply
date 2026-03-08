using System.Text.Json;

namespace LMSupply.Generator.Models;

/// <summary>
/// Defines a tool that the model can call.
/// Flat structure following industry conventions (OpenAI .NET SDK, Microsoft.Extensions.AI).
/// </summary>
/// <param name="Name">The function name.</param>
/// <param name="Description">Description of what the function does.</param>
/// <param name="Parameters">JSON Schema for the function parameters.</param>
public sealed record ChatToolDefinition(
    string Name,
    string? Description = null,
    JsonElement? Parameters = null);
