using System.Security.Cryptography;
using System.Text;
using LMSupply.Console.Host.Data;
using Microsoft.EntityFrameworkCore;

namespace LMSupply.Console.Host.Services;

/// <summary>
/// CRUD and statistics for API keys. Uses a pooled DbContext factory.
/// Thread-safe: each operation opens its own short-lived DbContext.
/// </summary>
public sealed partial class ApiKeyService(IDbContextFactory<ApiKeyDbContext> dbFactory, ILogger<ApiKeyService> logger)
{
    private volatile bool _authEnabled;
    private volatile bool _authStateInitialized;

    // ─── Key generation ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new API key. Returns the entity + the full plaintext key (shown once).
    /// </summary>
    public async Task<(ApiKeyResponse entity, string fullKey)> CreateKeyAsync(string name)
    {
        var rawKey = GenerateKey();
        var hash = HashKey(rawKey);
        var prefix = rawKey[..12]; // "lms-a1b2c3d4"

        var entity = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            KeyHash = hash,
            KeyPrefix = prefix,
            CreatedAt = DateTime.UtcNow,
        };

        await using var db = await dbFactory.CreateDbContextAsync();
        db.ApiKeys.Add(entity);
        await db.SaveChangesAsync();
        _authEnabled = true;
        _authStateInitialized = true;

        LogKeyCreated(logger, entity.Id, name);
        return (ToResponse(entity), rawKey);
    }

    // ─── Validation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the matching ApiKey if the token is valid, null otherwise.
    /// </summary>
    public async Task<ApiKey?> ValidateKeyAsync(string token)
    {
        if (string.IsNullOrEmpty(token) || !token.StartsWith("lms-", StringComparison.Ordinal))
            return null;

        var hash = HashKey(token);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.KeyHash == hash);
    }

    /// <summary>Returns true if ANY key exists (i.e., auth is enabled).</summary>
    public async Task<bool> AnyKeyExistsAsync()
    {
        if (_authStateInitialized)
            return _authEnabled;

        await using var db = await dbFactory.CreateDbContextAsync();
        _authEnabled = await db.ApiKeys.AnyAsync();
        _authStateInitialized = true;
        return _authEnabled;
    }

    // ─── CRUD ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ApiKeyResponse>> GetAllKeysAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var keys = await db.ApiKeys.OrderBy(k => k.CreatedAt).ToListAsync();
        return keys.Select(ToResponse).ToList();
    }

    /// <summary>Returns false if key not found.</summary>
    public async Task<bool> DeleteKeyAsync(Guid id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var key = await db.ApiKeys.FindAsync(id);
        if (key is null) return false;

        db.ApiKeys.Remove(key);
        await db.SaveChangesAsync();
        LogKeyDeleted(logger, id);

        // Recheck actual state (might still have other keys)
        await using var checkDb = await dbFactory.CreateDbContextAsync();
        _authEnabled = await checkDb.ApiKeys.AnyAsync();
        _authStateInitialized = true;

        return true;
    }

    // ─── Request logging ─────────────────────────────────────────────────────

    /// <summary>Logs a completed request and updates LastUsedAt + TotalRequests.</summary>
    public async Task LogRequestAsync(Guid keyId, string path, string method, int statusCode, long durationMs)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        db.ApiKeyRequests.Add(new ApiKeyRequest
        {
            ApiKeyId = keyId,
            Timestamp = DateTime.UtcNow,
            Path = path,
            Method = method,
            StatusCode = statusCode,
            DurationMs = durationMs,
        });

        await db.ApiKeys
            .Where(k => k.Id == keyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(k => k.LastUsedAt, DateTime.UtcNow)
                .SetProperty(k => k.TotalRequests, k => k.TotalRequests + 1));

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // ─── Statistics ──────────────────────────────────────────────────────────

    public async Task<ApiKeyStats> GetKeyStatsAsync(Guid keyId, int days)
        => await ComputeStatsAsync(db => db.ApiKeyRequests.Where(r => r.ApiKeyId == keyId), days);

    public async Task<ApiKeyStats> GetGlobalStatsAsync(int days)
        => await ComputeStatsAsync(db => db.ApiKeyRequests, days);

    private async Task<ApiKeyStats> ComputeStatsAsync(
        Func<ApiKeyDbContext, IQueryable<ApiKeyRequest>> queryFactory, int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = queryFactory(db).Where(r => r.Timestamp >= cutoff);

        var requests = await query.ToListAsync();

        var total = requests.Count;
        var errors = requests.Count(r => r.StatusCode >= 400);
        var errorRate = total > 0 ? (double)errors / total : 0.0;
        var avgDuration = total > 0 ? requests.Average(r => r.DurationMs) : 0.0;

        var byDay = requests
            .GroupBy(r => r.Timestamp.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(g => g.Key)
            .Select(g => new RequestsByDay(g.Key, g.Count()))
            .ToList();

        var byEndpoint = requests
            .GroupBy(r => r.Path)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new RequestsByEndpoint(g.Key, g.Count()))
            .ToList();

        return new ApiKeyStats(total, errorRate, avgDuration, byDay, byEndpoint);
    }

    // ─── Log retention cleanup ───────────────────────────────────────────────

    /// <summary>Deletes request logs older than 30 days. Called once at startup.</summary>
    public async Task CleanupOldLogsAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        await using var db = await dbFactory.CreateDbContextAsync();
        var deleted = await db.ApiKeyRequests
            .Where(r => r.Timestamp < cutoff)
            .ExecuteDeleteAsync();

        if (deleted > 0)
            LogLogsCleanedUp(logger, deleted);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(16); // 16 bytes = 32 hex chars
        return "lms-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HashKey(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ApiKeyResponse ToResponse(ApiKey k) => new(
        k.Id, k.Name, k.KeyPrefix, k.CreatedAt, k.LastUsedAt, k.TotalRequests);

    [LoggerMessage(Level = LogLevel.Information, Message = "API key created: {Id} ({Name})")]
    private static partial void LogKeyCreated(ILogger logger, Guid id, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "API key deleted: {Id}")]
    private static partial void LogKeyDeleted(ILogger logger, Guid id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} old request log entries")]
    private static partial void LogLogsCleanedUp(ILogger logger, int count);
}
