namespace LMSupply.Console.Host.Infrastructure.Vision;

public sealed record CocoDetectionResult
{
    public required IReadOnlyList<CocoImage> Images { get; init; }
    public required IReadOnlyList<CocoAnnotation> Annotations { get; init; }
    public required IReadOnlyList<CocoCategory> Categories { get; init; }
}

public sealed record CocoImage
{
    public int Id { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed record CocoAnnotation
{
    public int Id { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("image_id")]
    public int ImageId { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("category_id")]
    public int CategoryId { get; init; }
    public float[] Bbox { get; init; } = [];  // [x, y, width, height] COCO format
    public float Score { get; init; }
    public float Area { get; init; }
}

public sealed record CocoCategory
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string Supercategory { get; init; } = "object";
}

public static class CocoFormatter
{
    public static CocoDetectionResult Format<T>(
        IReadOnlyList<T> results,
        Func<T, string> getLabel,
        Func<T, float> getConfidence,
        Func<T, (float x1, float y1, float width, float height)> getBox,
        int imageWidth = 0,
        int imageHeight = 0)
    {
        var uniqueLabels = results.Select(getLabel).Distinct().ToList();
        var categories = uniqueLabels.Select((label, i) => new CocoCategory { Id = i + 1, Name = label }).ToList();
        var catMap = categories.ToDictionary(c => c.Name, c => c.Id);

        var annotations = results.Select((r, i) =>
        {
            var box = getBox(r);
            return new CocoAnnotation
            {
                Id = i + 1,
                ImageId = 1,
                CategoryId = catMap[getLabel(r)],
                Bbox = [box.x1, box.y1, box.width, box.height],
                Score = getConfidence(r),
                Area = box.width * box.height
            };
        }).ToList();

        return new CocoDetectionResult
        {
            Images = [new CocoImage { Id = 1, Width = imageWidth, Height = imageHeight }],
            Annotations = annotations,
            Categories = categories
        };
    }
}
