using LMSupply.Console.Host.Data;
using LMSupply.Console.Host.Services;

namespace LMSupply.Console.Host.Endpoints;

public static class ApiKeyEndpoints
{
    public static void MapApiKeyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/keys")
            .WithTags("ApiKeys");

        // GET /api/keys
        group.MapGet("/", async (ApiKeyService svc) =>
        {
            var keys = await svc.GetAllKeysAsync();
            return Results.Ok(keys);
        })
        .WithName("ListApiKeys")
        .WithSummary("List all API keys");

        // POST /api/keys
        group.MapPost("/", async (CreateKeyRequest req, ApiKeyService svc) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = new { message = "Name is required.", type = "invalid_request_error", code = "invalid_value" } });

            var (entity, fullKey) = await svc.CreateKeyAsync(req.Name.Trim());
            var created = new ApiKeyCreatedResponse(entity.Id, entity.Name, fullKey, entity.KeyPrefix, entity.CreatedAt);
            return Results.Created($"/api/keys/{entity.Id}", created);
        })
        .WithName("CreateApiKey")
        .WithSummary("Create a new API key (returns full key once)");

        // DELETE /api/keys/{id}
        group.MapDelete("/{id:guid}", async (Guid id, ApiKeyService svc) =>
        {
            var deleted = await svc.DeleteKeyAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteApiKey")
        .WithSummary("Delete an API key");

        // GET /api/keys/stats?days=7
        group.MapGet("/stats", async (ApiKeyService svc, int days = 7) =>
        {
            var stats = await svc.GetGlobalStatsAsync(days);
            return Results.Ok(stats);
        })
        .WithName("GetGlobalApiKeyStats")
        .WithSummary("Get aggregated request statistics for all keys");

        // GET /api/keys/{id}/stats?days=7
        group.MapGet("/{id:guid}/stats", async (Guid id, ApiKeyService svc, int days = 7) =>
        {
            var stats = await svc.GetKeyStatsAsync(id, days);
            return Results.Ok(stats);
        })
        .WithName("GetApiKeyStats")
        .WithSummary("Get request statistics for a specific key");
    }
}
