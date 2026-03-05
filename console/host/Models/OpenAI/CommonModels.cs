using System.Text.Json.Serialization;

namespace LMSupply.Console.Host.Models.OpenAI;

/// <summary>
/// OpenAI API compatible error response
/// </summary>
public sealed record ErrorResponse
{
    public required ErrorDetail Error { get; init; }
}

public sealed record ErrorDetail
{
    public required string Message { get; init; }
    public required string Type { get; init; }
    public string? Param { get; init; }
    public string? Code { get; init; }
}

/// <summary>
/// OpenAI API compatible usage information
/// </summary>
public sealed record Usage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}

/// <summary>
/// Model information (OpenAI /v1/models compatible)
/// </summary>
public sealed record ModelInfo
{
    public required string Id { get; init; }
    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "model";
    public long Created { get; init; } = 1700000000;
    [JsonPropertyName("owned_by")]
    public string OwnedBy { get; init; } = "lm-supply";
    public required IReadOnlyList<string> Capabilities { get; init; }
    [JsonPropertyName("context_length")]
    public int? ContextLength { get; init; }
}

/// <summary>
/// Model list response (OpenAI /v1/models compatible)
/// </summary>
public sealed record ModelList
{
    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "list";
    public required IReadOnlyList<ModelInfo> Data { get; init; }
}
