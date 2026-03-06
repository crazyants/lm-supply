using System.Diagnostics;
using System.Text.Json;
using LMSupply.Console.Host.Services;

namespace LMSupply.Console.Host.Infrastructure;

/// <summary>
/// Enforces API key authentication when any key exists.
/// If no keys exist, all requests pass through (unlimited mode).
/// Management endpoints (/api/keys/*) are always exempt.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, ApiKeyService apiKeyService)
{
    // These path prefixes never require a key (management UI access)
    private static readonly string[] ExemptPrefixes = ["/api/keys", "/swagger", "/health"];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Always exempt management and infrastructure endpoints
        if (IsExempt(path))
        {
            await next(context);
            return;
        }

        // If no keys exist, unlimited access
        if (!await apiKeyService.AnyKeyExistsAsync())
        {
            await next(context);
            return;
        }

        // Extract Bearer token
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        var token = ExtractBearerToken(authHeader);

        if (token is null)
        {
            await WriteUnauthorizedAsync(context, "No API key provided. Pass 'Authorization: Bearer lms-...' header.");
            return;
        }

        var key = await apiKeyService.ValidateKeyAsync(token);
        if (key is null)
        {
            await WriteUnauthorizedAsync(context, "Invalid API key.");
            return;
        }

        // Attach key info and measure request time
        context.Items["ApiKeyId"] = key.Id;
        var sw = Stopwatch.StartNew();

        await next(context);

        sw.Stop();

        // Fire-and-forget log (non-blocking)
        _ = apiKeyService.LogRequestAsync(
            key.Id,
            path,
            context.Request.Method,
            context.Response.StatusCode,
            sw.ElapsedMilliseconds);
    }

    private static bool IsExempt(string path)
    {
        foreach (var prefix in ExemptPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? ExtractBearerToken(string? authHeader)
    {
        if (authHeader is null || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        return authHeader["Bearer ".Length..].Trim();
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new
        {
            error = new { message, type = "auth_error", code = "invalid_api_key" }
        });
        await context.Response.WriteAsync(body);
    }
}
