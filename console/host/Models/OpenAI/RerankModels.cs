using System.Text.Json;
using System.Text.Json.Serialization;

namespace LMSupply.Console.Host.Models.OpenAI;

/// <summary>
/// Rerank request (Cohere API compatible)
/// </summary>
public sealed record RerankRequest
{
    /// <summary>
    /// Model ID
    /// </summary>
    public string Model { get; init; } = "default";

    /// <summary>
    /// Query to match documents against
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Documents to rerank. Can be string[] or object[] (for structured documents with rank_fields).
    /// </summary>
    public required JsonElement Documents { get; init; }

    /// <summary>
    /// Number of top results to return
    /// </summary>
    [JsonPropertyName("top_n")]
    public int? TopN { get; init; }

    /// <summary>
    /// Whether to return documents in the response
    /// </summary>
    [JsonPropertyName("return_documents")]
    public bool ReturnDocuments { get; init; } = true;

    /// <summary>
    /// Fields to use for ranking when documents are objects. Multiple fields are joined with a space.
    /// </summary>
    [JsonPropertyName("rank_fields")]
    public IReadOnlyList<string>? RankFields { get; init; }

    /// <summary>
    /// Maximum number of chunks per document for long-document reranking.
    /// </summary>
    [JsonPropertyName("max_chunks_per_doc")]
    public int? MaxChunksPerDoc { get; init; }
}

/// <summary>
/// Rerank response
/// </summary>
public sealed record RerankResponse
{
    public required string Id { get; init; }
    public required string Model { get; init; }
    public required IReadOnlyList<RerankResult> Results { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RerankMeta? Meta { get; init; }
}

/// <summary>
/// Rerank metadata (Cohere-compatible)
/// </summary>
public sealed record RerankMeta
{
    [JsonPropertyName("api_version")]
    public RerankApiVersion ApiVersion { get; init; } = new();
    [JsonPropertyName("billed_units")]
    public RerankBilledUnits BilledUnits { get; init; } = new();
    public RerankTokens Tokens { get; init; } = new();
}

public sealed record RerankApiVersion
{
    public string Version { get; init; } = "2";
}

public sealed record RerankBilledUnits
{
    [JsonPropertyName("search_units")]
    public int SearchUnits { get; init; } = 1;
}

public sealed record RerankTokens
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }
}

/// <summary>
/// Individual rerank result
/// </summary>
public sealed record RerankResult
{
    public int Index { get; init; }
    [JsonPropertyName("relevance_score")]
    public float RelevanceScore { get; init; }
    public RerankDocument? Document { get; init; }
}

/// <summary>
/// Reranked document
/// </summary>
public sealed record RerankDocument
{
    public required string Text { get; init; }
}
