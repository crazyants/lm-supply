using LMSupply.Console.Host.Infrastructure;
using LMSupply.Console.Host.Models.OpenAI;
using LMSupply.Console.Host.Services;
using RerankRequest = LMSupply.Console.Host.Models.OpenAI.RerankRequest;

namespace LMSupply.Console.Host.Endpoints;

public static class RerankEndpoints
{
    public static void MapRerankEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/v1")
            .WithTags("Rerank")
            ;

        // POST /v1/rerank - Cohere API compatible
        group.MapPost("/rerank", async (RerankRequest request, ModelManagerService manager, CancellationToken ct) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Query))
                {
                    return ApiHelper.Error("'query' field is required");
                }

                // Parse documents from JsonElement: supports string[] and object[]
                if (request.Documents.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return ApiHelper.Error("'documents' must be an array");
                }

                var documents = new List<string>();
                foreach (var doc in request.Documents.EnumerateArray())
                {
                    if (doc.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        documents.Add(doc.GetString()!);
                    }
                    else if (doc.ValueKind == System.Text.Json.JsonValueKind.Object && request.RankFields?.Count > 0)
                    {
                        // Join specified fields for structured documents
                        var parts = request.RankFields
                            .Where(f => doc.TryGetProperty(f, out _))
                            .Select(f => doc.GetProperty(f).GetString() ?? "");
                        documents.Add(string.Join(" ", parts));
                    }
                    else
                    {
                        // Fall back to raw JSON text for unknown object types
                        documents.Add(doc.GetRawText());
                    }
                }

                if (documents.Count == 0)
                {
                    return ApiHelper.Error("'documents' field is required and must not be empty");
                }

                await using var scope = await manager.GetRerankerAsync(request.Model, ct);
                var results = await scope.Model.RerankAsync(
                    request.Query,
                    documents,
                    request.TopN,
                    ct);

                var id = ApiHelper.GenerateId("rerank");

                // Rough token estimate for meta
                var inputTokens = (request.Query.Length + documents.Sum(d => d.Length)) / 4;

                return Results.Ok(new RerankResponse
                {
                    Id = id,
                    Model = scope.Model.ModelId,
                    Results = results.Select(r => new RerankResult
                    {
                        Index = r.OriginalIndex,
                        RelevanceScore = r.Score,
                        Document = request.ReturnDocuments
                            ? new RerankDocument { Text = r.Document }
                            : null
                    }).ToList(),
                    Meta = new RerankMeta
                    {
                        Tokens = new RerankTokens { InputTokens = inputTokens }
                    }
                });
            }
            catch (Exception ex)
            {
                return ApiHelper.InternalError(ex);
            }
        })
        .WithName("Rerank")
        .WithSummary("Rerank documents by relevance (Cohere API compatible)")
        .WithDescription("Ranks documents based on their relevance to a query. Compatible with Cohere's rerank API.")
        .Produces<RerankResponse>()
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(404)
        .Produces<ErrorResponse>(500);
    }
}
