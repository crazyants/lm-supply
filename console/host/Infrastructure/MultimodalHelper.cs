using System.Globalization;
using System.Text;
using System.Text.Json;
using LMSupply.Console.Host.Models.OpenAI;
using LMSupply.Console.Host.Services;

namespace LMSupply.Console.Host.Infrastructure;

public static class MultimodalHelper
{
    private static readonly HttpClient _http = new();

    /// <summary>
    /// Extracts text from a message Content field (string or ContentPart[]).
    /// Image parts are replaced with "[Image: {caption}]" via the Captioner.
    /// </summary>
    public static async Task<string> ExtractTextAsync(
        JsonElement content,
        ModelManagerService manager,
        CancellationToken ct)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";

        if (content.ValueKind != JsonValueKind.Array)
            return "";

        var sb = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            var type = part.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            if (type == "text" && part.TryGetProperty("text", out var textProp))
            {
                sb.Append(textProp.GetString());
            }
            else if (type == "image_url" && part.TryGetProperty("image_url", out var imgProp))
            {
                var url = imgProp.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                var imageBytes = await LoadImageBytesAsync(url, ct);
                if (imageBytes != null)
                {
                    try
                    {
                        await using var scope = await manager.GetCaptionerAsync("default", ct);
                        var result = await scope.Model.CaptionAsync(imageBytes, ct);
                        sb.Append(CultureInfo.InvariantCulture, $"[Image: {result.Caption}]");
                    }
                    catch
                    {
                        sb.Append("[Image: unable to caption]");
                    }
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>Returns true if any message contains image_url content parts.</summary>
    public static bool HasImageContent(IReadOnlyList<ChatCompletionMessage> messages)
        => messages.Any(m =>
            m.Content.HasValue &&
            m.Content.Value.ValueKind == JsonValueKind.Array &&
            m.Content.Value.EnumerateArray().Any(p =>
                p.TryGetProperty("type", out var t) && t.GetString() == "image_url"));

    private static async Task<byte[]?> LoadImageBytesAsync(string url, CancellationToken ct)
    {
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = url.IndexOf(',');
            if (comma < 0) return null;
            try { return Convert.FromBase64String(url[(comma + 1)..]); }
            catch { return null; }
        }

        // Only allow localhost URLs for security
        if (url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase))
        {
            try { return await _http.GetByteArrayAsync(url, ct); }
            catch { return null; }
        }

        return null;
    }
}
