namespace LMSupply.Console.Host.Data;

/// <summary>API key entity stored in SQLite.</summary>
public sealed class ApiKey
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>SHA-256 of the full key (hex string).</summary>
    public string KeyHash { get; set; } = "";
    /// <summary>First 12 chars of the key for display (e.g., "lms-a1b2c3d4").</summary>
    public string KeyPrefix { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    /// <summary>Denormalized total request count for fast list display.</summary>
    public long TotalRequests { get; set; }

    public ICollection<ApiKeyRequest> Requests { get; set; } = [];
}

/// <summary>Per-request log row.</summary>
public sealed class ApiKeyRequest
{
    public long Id { get; set; }
    public Guid ApiKeyId { get; set; }
    public ApiKey ApiKey { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public string Path { get; set; } = "";
    public string Method { get; set; } = "";
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
}

// ─── DTOs ───────────────────────────────────────────────────────────────────

public record CreateKeyRequest(string Name);

public record ApiKeyResponse(
    Guid Id,
    string Name,
    string KeyPrefix,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    long TotalRequests);

/// <summary>Returned only on creation — full key shown once.</summary>
public record ApiKeyCreatedResponse(
    Guid Id,
    string Name,
    string Key,
    string KeyPrefix,
    DateTime CreatedAt);

public record RequestsByDay(string Date, long Count);
public record RequestsByEndpoint(string Path, long Count);

public record ApiKeyStats(
    long TotalRequests,
    double ErrorRate,
    double AvgDurationMs,
    IReadOnlyList<RequestsByDay> RequestsByDay,
    IReadOnlyList<RequestsByEndpoint> RequestsByEndpoint);
