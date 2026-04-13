namespace LMSupply.Generator.Models;

/// <summary>
/// A content part within a multimodal message.
/// Messages can contain a mix of text and image content parts.
/// </summary>
public abstract record ContentPart
{
    /// <summary>
    /// The type identifier for serialization (e.g., "text", "image_url").
    /// </summary>
    public abstract string Type { get; }
}

/// <summary>
/// A text content part.
/// </summary>
public sealed record TextContentPart(string Text) : ContentPart
{
    /// <inheritdoc />
    public override string Type => "text";
}

/// <summary>
/// An image content part using a URL or base64 data URI.
/// Compatible with the OpenAI vision API format.
/// </summary>
public sealed record ImageContentPart : ContentPart
{
    /// <inheritdoc />
    public override string Type => "image_url";

    /// <summary>
    /// The image URL. Can be:
    /// - An HTTP(S) URL to a remote image
    /// - A base64 data URI (e.g., "data:image/jpeg;base64,...")
    /// </summary>
    public required string Url { get; init; }
}
