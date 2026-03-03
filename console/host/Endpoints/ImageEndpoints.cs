using LMSupply.Console.Host.Infrastructure;
using LMSupply.Console.Host.Models.OpenAI;
using LMSupply.Console.Host.Services;
using LMSupply.ImageGenerator;

namespace LMSupply.Console.Host.Endpoints;

public static class ImageEndpoints
{
    public static void MapImageEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/v1/images")
            .WithTags("Images")
            ;

        // POST /v1/images/generations - OpenAI compatible
        group.MapPost("/generations", async (
            ImageGenerationRequest request,
            ModelManagerService manager,
            CancellationToken ct) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return ApiHelper.Error("'prompt' field is required");
                }

                // Parse and validate size BEFORE model loading
                var (width, height) = ParseSize(request.Size);
                if (width % 8 != 0 || height % 8 != 0)
                {
                    return ApiHelper.Error($"Image dimensions must be divisible by 8. Got {width}x{height}.");
                }
                if (width < 64 || width > 2048 || height < 64 || height > 2048)
                {
                    return ApiHelper.Error($"Image dimensions must be between 64 and 2048. Got {width}x{height}.");
                }

                var modelId = request.Model ?? "default";
                await using var scope = await manager.GetImageGeneratorAsync(modelId, ct);

                var options = new GenerationOptions
                {
                    Width = width,
                    Height = height,
                    Steps = request.Steps ?? 4,
                    GuidanceScale = request.GuidanceScale ?? 1.0f,
                    Seed = request.Seed,
                    NegativePrompt = request.NegativePrompt
                };

                var result = await scope.Model.GenerateAsync(request.Prompt, options, ct);

                // Convert to OpenAI-compatible response
                var imageData = new GeneratedImageData
                {
                    B64Json = Convert.ToBase64String(result.ImageData),
                    RevisedPrompt = result.Prompt
                };

                return Results.Ok(new ImageGenerationResponse
                {
                    Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Data = [imageData]
                });
            }
            catch (Exception ex)
            {
                return ApiHelper.InternalError(ex);
            }
        })
        .WithName("CreateImage")
        .WithSummary("Generate images from text (OpenAI compatible)")
        .WithDescription("Creates an image given a text prompt using Latent Consistency Models for fast generation.")
        .Produces<ImageGenerationResponse>()
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(500);

        // POST /v1/images/generate - Extended API with metadata
        group.MapPost("/generate", async (
            ImageGenerationRequest request,
            ModelManagerService manager,
            CancellationToken ct) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return ApiHelper.Error("'prompt' field is required");
                }

                // Parse and validate size BEFORE model loading
                var (width, height) = ParseSize(request.Size);
                if (width % 8 != 0 || height % 8 != 0)
                {
                    return ApiHelper.Error($"Image dimensions must be divisible by 8. Got {width}x{height}.");
                }
                if (width < 64 || width > 2048 || height < 64 || height > 2048)
                {
                    return ApiHelper.Error($"Image dimensions must be between 64 and 2048. Got {width}x{height}.");
                }

                var modelId = request.Model ?? "default";
                await using var scope = await manager.GetImageGeneratorAsync(modelId, ct);

                var options = new GenerationOptions
                {
                    Width = width,
                    Height = height,
                    Steps = request.Steps ?? 4,
                    GuidanceScale = request.GuidanceScale ?? 1.0f,
                    Seed = request.Seed,
                    NegativePrompt = request.NegativePrompt
                };

                var result = await scope.Model.GenerateAsync(request.Prompt, options, ct);

                return Results.Ok(new ImageGenerationExtendedResponse
                {
                    Id = ApiHelper.GenerateId("img"),
                    Model = scope.Model.ModelId,
                    Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    GenerationTimeMs = (long)result.GenerationTime.TotalMilliseconds,
                    Data =
                    [
                        new GeneratedImageExtendedData
                        {
                            B64Json = Convert.ToBase64String(result.ImageData),
                            Width = result.Width,
                            Height = result.Height,
                            Seed = result.Seed,
                            Steps = result.Steps,
                            Prompt = result.Prompt
                        }
                    ]
                });
            }
            catch (Exception ex)
            {
                return ApiHelper.InternalError(ex);
            }
        })
        .WithName("GenerateImage")
        .WithSummary("Generate images with extended metadata")
        .WithDescription("Creates an image with detailed generation metadata including seed, steps, and timing.")
        .Produces<ImageGenerationExtendedResponse>()
        .Produces<ErrorResponse>(400)
        .Produces<ErrorResponse>(500);

        // GET /v1/images/models - List available models
        group.MapGet("/models", () =>
        {
            var registry = LMSupply.ImageGenerator.Models.ImageGeneratorModelRegistry.Default;
            var models = registry.GetAvailableModels().Select(info =>
            {
                return new
                {
                    id = info.AliasName,
                    repo_id = info.ModelId,
                    recommended_steps = info.RecommendedSteps,
                    recommended_guidance_scale = info.RecommendedGuidanceScale
                };
            }).ToList();

            return Results.Ok(new { models });
        })
        .WithName("ListImageModels")
        .WithSummary("List available image generation models")
        .WithDescription("Returns a list of available model aliases and their recommended settings.")
        .Produces(200);
    }

    private static (int width, int height) ParseSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            return (512, 512); // Default size
        }

        var parts = size.ToLowerInvariant().Split('x');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var w) &&
            int.TryParse(parts[1], out var h))
        {
            return (w, h);
        }

        // Common size presets
        return size.ToLowerInvariant() switch
        {
            "256x256" => (256, 256),
            "512x512" => (512, 512),
            "768x768" => (768, 768),
            "1024x1024" => (1024, 1024),
            "1024x1792" => (1024, 1792),
            "1792x1024" => (1792, 1024),
            _ => (512, 512)
        };
    }
}
