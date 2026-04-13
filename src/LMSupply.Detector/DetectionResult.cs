namespace LMSupply.Detector;

/// <summary>
/// Represents a single detected object in an image.
/// </summary>
/// <param name="ClassId">The class ID from COCO or custom dataset (0-indexed).</param>
/// <param name="Label">Human-readable class label (e.g., "person", "car").</param>
/// <param name="Confidence">Detection confidence score (0.0 to 1.0).</param>
/// <param name="Box">Bounding box coordinates.</param>
/// <param name="Keypoints">Optional keypoint data for pose estimation models. Null for standard object detection.</param>
public readonly record struct DetectionResult(
    int ClassId,
    string Label,
    float Confidence,
    BoundingBox Box,
    IReadOnlyList<Keypoint>? Keypoints = null)
{
    /// <summary>
    /// Gets whether this detection includes keypoint (pose) data.
    /// </summary>
    public bool HasKeypoints => Keypoints is { Count: > 0 };

    /// <summary>
    /// Returns a string representation of the detection.
    /// </summary>
    public override string ToString() =>
        HasKeypoints
            ? $"{Label} ({Confidence:P1}) at [{Box.X1:F0}, {Box.Y1:F0}, {Box.X2:F0}, {Box.Y2:F0}] ({Keypoints!.Count} keypoints)"
            : $"{Label} ({Confidence:P1}) at [{Box.X1:F0}, {Box.Y1:F0}, {Box.X2:F0}, {Box.Y2:F0}]";
}

/// <summary>
/// Represents a single keypoint in a pose estimation result.
/// </summary>
/// <param name="X">X coordinate in original image pixels.</param>
/// <param name="Y">Y coordinate in original image pixels.</param>
/// <param name="Confidence">Keypoint visibility/confidence score (0.0 to 1.0).</param>
public readonly record struct Keypoint(float X, float Y, float Confidence)
{
    /// <summary>
    /// Gets whether this keypoint is considered visible (confidence above threshold).
    /// </summary>
    public bool IsVisible(float threshold = 0.5f) => Confidence >= threshold;

    /// <summary>
    /// Returns a string representation of the keypoint.
    /// </summary>
    public override string ToString() => $"({X:F1}, {Y:F1}, {Confidence:F2})";
}

/// <summary>
/// COCO 17-keypoint skeleton indices for pose estimation.
/// </summary>
public static class PoseSkeleton
{
    public const int Nose = 0;
    public const int LeftEye = 1;
    public const int RightEye = 2;
    public const int LeftEar = 3;
    public const int RightEar = 4;
    public const int LeftShoulder = 5;
    public const int RightShoulder = 6;
    public const int LeftElbow = 7;
    public const int RightElbow = 8;
    public const int LeftWrist = 9;
    public const int RightWrist = 10;
    public const int LeftHip = 11;
    public const int RightHip = 12;
    public const int LeftKnee = 13;
    public const int RightKnee = 14;
    public const int LeftAnkle = 15;
    public const int RightAnkle = 16;

    /// <summary>
    /// Total number of COCO keypoints.
    /// </summary>
    public const int Count = 17;

    /// <summary>
    /// COCO skeleton bone connections (pairs of keypoint indices).
    /// </summary>
    public static IReadOnlyList<(int From, int To)> Bones { get; } =
    [
        (Nose, LeftEye), (Nose, RightEye),
        (LeftEye, LeftEar), (RightEye, RightEar),
        (LeftShoulder, RightShoulder),
        (LeftShoulder, LeftElbow), (RightShoulder, RightElbow),
        (LeftElbow, LeftWrist), (RightElbow, RightWrist),
        (LeftShoulder, LeftHip), (RightShoulder, RightHip),
        (LeftHip, RightHip),
        (LeftHip, LeftKnee), (RightHip, RightKnee),
        (LeftKnee, LeftAnkle), (RightKnee, RightAnkle)
    ];

    /// <summary>
    /// COCO keypoint names.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "nose", "left_eye", "right_eye", "left_ear", "right_ear",
        "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
        "left_wrist", "right_wrist", "left_hip", "right_hip",
        "left_knee", "right_knee", "left_ankle", "right_ankle"
    ];
}

/// <summary>
/// Represents a bounding box with pixel coordinates.
/// </summary>
/// <param name="X1">Left edge X coordinate.</param>
/// <param name="Y1">Top edge Y coordinate.</param>
/// <param name="X2">Right edge X coordinate.</param>
/// <param name="Y2">Bottom edge Y coordinate.</param>
public readonly record struct BoundingBox(float X1, float Y1, float X2, float Y2)
{
    /// <summary>
    /// Gets the width of the bounding box.
    /// </summary>
    public float Width => X2 - X1;

    /// <summary>
    /// Gets the height of the bounding box.
    /// </summary>
    public float Height => Y2 - Y1;

    /// <summary>
    /// Gets the center X coordinate.
    /// </summary>
    public float CenterX => (X1 + X2) / 2;

    /// <summary>
    /// Gets the center Y coordinate.
    /// </summary>
    public float CenterY => (Y1 + Y2) / 2;

    /// <summary>
    /// Gets the area of the bounding box.
    /// </summary>
    public float Area => Width * Height;

    /// <summary>
    /// Calculates Intersection over Union (IoU) with another box.
    /// </summary>
    /// <param name="other">The other bounding box.</param>
    /// <returns>IoU value between 0 and 1.</returns>
    public float IoU(BoundingBox other)
    {
        var intersectX1 = Math.Max(X1, other.X1);
        var intersectY1 = Math.Max(Y1, other.Y1);
        var intersectX2 = Math.Min(X2, other.X2);
        var intersectY2 = Math.Min(Y2, other.Y2);

        if (intersectX1 >= intersectX2 || intersectY1 >= intersectY2)
            return 0f;

        var intersectionArea = (intersectX2 - intersectX1) * (intersectY2 - intersectY1);
        var unionArea = Area + other.Area - intersectionArea;

        return unionArea > 0 ? intersectionArea / unionArea : 0f;
    }

    /// <summary>
    /// Creates a bounding box from center coordinates and dimensions.
    /// </summary>
    /// <param name="cx">Center X coordinate.</param>
    /// <param name="cy">Center Y coordinate.</param>
    /// <param name="width">Box width.</param>
    /// <param name="height">Box height.</param>
    /// <returns>A new bounding box.</returns>
    public static BoundingBox FromCenterSize(float cx, float cy, float width, float height) =>
        new(cx - width / 2, cy - height / 2, cx + width / 2, cy + height / 2);

    /// <summary>
    /// Scales the bounding box to target image dimensions.
    /// </summary>
    /// <param name="scaleX">X scale factor.</param>
    /// <param name="scaleY">Y scale factor.</param>
    /// <returns>Scaled bounding box.</returns>
    public BoundingBox Scale(float scaleX, float scaleY) =>
        new(X1 * scaleX, Y1 * scaleY, X2 * scaleX, Y2 * scaleY);

    /// <summary>
    /// Clamps the bounding box to image boundaries.
    /// </summary>
    /// <param name="imageWidth">Image width.</param>
    /// <param name="imageHeight">Image height.</param>
    /// <returns>Clamped bounding box.</returns>
    public BoundingBox Clamp(int imageWidth, int imageHeight) =>
        new(
            Math.Max(0, Math.Min(X1, imageWidth)),
            Math.Max(0, Math.Min(Y1, imageHeight)),
            Math.Max(0, Math.Min(X2, imageWidth)),
            Math.Max(0, Math.Min(Y2, imageHeight)));
}
