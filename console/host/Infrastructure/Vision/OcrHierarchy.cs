namespace LMSupply.Console.Host.Infrastructure.Vision;

public sealed record OcrPage
{
    public int Width { get; init; }
    public int Height { get; init; }
    public required IReadOnlyList<OcrBlock> Blocks { get; init; }
}

public sealed record OcrBlock
{
    public required string Text { get; init; }
    public float Confidence { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("bounding_box")]
    public required OcrBoundingBox BoundingBox { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OcrLine>? Lines { get; init; }
}

public sealed record OcrLine
{
    public required string Text { get; init; }
    public float Confidence { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("bounding_box")]
    public required OcrBoundingBox BoundingBox { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OcrWord>? Words { get; init; }
}

public sealed record OcrWord
{
    public required string Text { get; init; }
    public float Confidence { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("bounding_box")]
    public required OcrBoundingBox BoundingBox { get; init; }
}

public sealed record OcrBoundingBox
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
}
