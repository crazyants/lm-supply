using LMSupply.Console.Host.Services;

namespace LMSupply.Console.Host.Endpoints;

public static class FileEndpoints
{
    public static void MapFileEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/images/files/{id}", (string id, TempFileService svc) =>
        {
            if (svc.TryGetFile(id, out var path) && path != null)
                return Results.File(path, "image/png");
            return Results.NotFound();
        })
        .WithName("GetTempImageFile")
        .WithSummary("Download a generated image by ID (TTL: 1 hour)")
        .WithTags("Images");
    }
}
