# API Key Feature Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add optional API key authentication to the console host with SQLite-backed request logging and statistics.

**Architecture:** Middleware-based auth (ApiKeyMiddleware) checks if any keys exist in SQLite DB; if none → pass through, if any → require `Authorization: Bearer lms-<32hex>`. EF Core SQLite stores keys (hashed) and per-request logs. React UI adds `/api-keys` page for management.

**Tech Stack:** EF Core 10 + SQLite (Microsoft.EntityFrameworkCore.Sqlite), Zustand (existing), Lucide React icons (existing), Tailwind CSS (existing).

---

## Task 1: Add NuGet packages + EF Core setup

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `console/host/LMSupply.Console.Host.csproj`

**Step 1: Add package versions to Directory.Packages.props**

In the `<!-- Console Host -->` ItemGroup, add after the last entry:
```xml
<PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.3" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.3" />
```

**Step 2: Reference packages in csproj**

Add to the existing `<ItemGroup>` with Swashbuckle in `LMSupply.Console.Host.csproj`:
```xml
<!-- EF Core SQLite for API key storage -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
```

**Step 3: Verify build**
```bash
dotnet build console/host/LMSupply.Console.Host.csproj
```
Expected: Build succeeded, no errors.

**Step 4: Commit**
```bash
git add Directory.Packages.props console/host/LMSupply.Console.Host.csproj
git commit -m "feat(console): add EF Core SQLite packages for API key storage"
```

---

## Task 2: Data models and DbContext

**Files:**
- Create: `console/host/Data/ApiKeyModels.cs`
- Create: `console/host/Data/ApiKeyDbContext.cs`

**Step 1: Create `console/host/Data/ApiKeyModels.cs`**

```csharp
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
```

**Step 2: Create `console/host/Data/ApiKeyDbContext.cs`**

```csharp
using Microsoft.EntityFrameworkCore;

namespace LMSupply.Console.Host.Data;

public sealed class ApiKeyDbContext(DbContextOptions<ApiKeyDbContext> options)
    : DbContext(options)
{
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ApiKeyRequest> ApiKeyRequests => Set<ApiKeyRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiKey>(e =>
        {
            e.HasKey(k => k.Id);
            e.HasIndex(k => k.KeyHash).IsUnique();
            e.Property(k => k.CreatedAt).HasConversion(
                v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            e.Property(k => k.LastUsedAt).HasConversion(
                v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
        });

        modelBuilder.Entity<ApiKeyRequest>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.ApiKeyId);
            e.HasIndex(r => r.Timestamp);
            e.Property(r => r.Timestamp).HasConversion(
                v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            e.HasOne(r => r.ApiKey)
             .WithMany(k => k.Requests)
             .HasForeignKey(r => r.ApiKeyId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

**Step 3: Build to verify models compile**
```bash
dotnet build console/host/LMSupply.Console.Host.csproj
```
Expected: Build succeeded.

**Step 4: Commit**
```bash
git add console/host/Data/
git commit -m "feat(console): add ApiKey data models and EF Core DbContext"
```

---

## Task 3: ApiKeyService

**Files:**
- Create: `console/host/Services/ApiKeyService.cs`

**Step 1: Create `console/host/Services/ApiKeyService.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using LMSupply.Console.Host.Data;
using Microsoft.EntityFrameworkCore;

namespace LMSupply.Console.Host.Services;

/// <summary>
/// CRUD and statistics for API keys. Uses a pooled DbContext factory.
/// Thread-safe: each operation opens its own short-lived DbContext.
/// </summary>
public sealed class ApiKeyService(IDbContextFactory<ApiKeyDbContext> dbFactory, ILogger<ApiKeyService> logger)
{
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

        LogKeyCreated(logger, entity.Id, name);
        return (ToResponse(entity), rawKey);
    }

    // ─── Validation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the matching ApiKey if the token is valid, null otherwise.
    /// Does NOT update LastUsedAt here — that's done after the request in the middleware.
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
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ApiKeys.AnyAsync();
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
        return true;
    }

    // ─── Request logging ─────────────────────────────────────────────────────

    /// <summary>Logs a completed request and updates LastUsedAt + TotalRequests.</summary>
    public async Task LogRequestAsync(Guid keyId, string path, string method, int statusCode, long durationMs)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

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
            .GroupBy(r => r.Timestamp.Date.ToString("yyyy-MM-dd"))
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
```

Note: `LoggerMessage` with `partial` requires the class to be `partial`. Adjust the class declaration:
```csharp
public sealed partial class ApiKeyService(...)
```

**Step 2: Build**
```bash
dotnet build console/host/LMSupply.Console.Host.csproj
```
Expected: Build succeeded.

**Step 3: Commit**
```bash
git add console/host/Services/ApiKeyService.cs
git commit -m "feat(console): add ApiKeyService with CRUD, validation, logging, and stats"
```

---

## Task 4: ApiKeyMiddleware

**Files:**
- Create: `console/host/Infrastructure/ApiKeyMiddleware.cs`

**Step 1: Create `console/host/Infrastructure/ApiKeyMiddleware.cs`**

```csharp
using System.Diagnostics;
using System.Text.Json;
using LMSupply.Console.Host.Data;
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
```

**Step 2: Build**
```bash
dotnet build console/host/LMSupply.Console.Host.csproj
```

**Step 3: Commit**
```bash
git add console/host/Infrastructure/ApiKeyMiddleware.cs
git commit -m "feat(console): add ApiKeyMiddleware for optional Bearer token enforcement"
```

---

## Task 5: API endpoints

**Files:**
- Create: `console/host/Endpoints/ApiKeyEndpoints.cs`

**Step 1: Create `console/host/Endpoints/ApiKeyEndpoints.cs`**

```csharp
using LMSupply.Console.Host.Data;
using LMSupply.Console.Host.Services;

namespace LMSupply.Console.Host.Endpoints;

public static class ApiKeyEndpoints
{
    public static void MapApiKeyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/keys")
            .WithTags("ApiKeys");

        // GET /api/keys
        group.MapGet("/", async (ApiKeyService svc) =>
        {
            var keys = await svc.GetAllKeysAsync();
            return Results.Ok(keys);
        })
        .WithName("ListApiKeys")
        .WithSummary("List all API keys");

        // POST /api/keys
        group.MapPost("/", async (CreateKeyRequest req, ApiKeyService svc) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = new { message = "Name is required.", type = "invalid_request_error", code = "invalid_value" } });

            var (entity, fullKey) = await svc.CreateKeyAsync(req.Name.Trim());
            var created = new ApiKeyCreatedResponse(entity.Id, entity.Name, fullKey, entity.KeyPrefix, entity.CreatedAt);
            return Results.Created($"/api/keys/{entity.Id}", created);
        })
        .WithName("CreateApiKey")
        .WithSummary("Create a new API key (returns full key once)");

        // DELETE /api/keys/{id}
        group.MapDelete("/{id:guid}", async (Guid id, ApiKeyService svc) =>
        {
            var deleted = await svc.DeleteKeyAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteApiKey")
        .WithSummary("Delete an API key");

        // GET /api/keys/stats?days=7
        group.MapGet("/stats", async (ApiKeyService svc, int days = 7) =>
        {
            var stats = await svc.GetGlobalStatsAsync(days);
            return Results.Ok(stats);
        })
        .WithName("GetGlobalApiKeyStats")
        .WithSummary("Get aggregated request statistics for all keys");

        // GET /api/keys/{id}/stats?days=7
        group.MapGet("/{id:guid}/stats", async (Guid id, ApiKeyService svc, int days = 7) =>
        {
            var stats = await svc.GetKeyStatsAsync(id, days);
            return Results.Ok(stats);
        })
        .WithName("GetApiKeyStats")
        .WithSummary("Get request statistics for a specific key");
    }
}
```

**Step 2: Build**
```bash
dotnet build console/host/LMSupply.Console.Host.csproj
```

**Step 3: Commit**
```bash
git add console/host/Endpoints/ApiKeyEndpoints.cs
git commit -m "feat(console): add API key management endpoints (/api/keys)"
```

---

## Task 6: Wire up in Program.cs

**Files:**
- Modify: `console/host/Program.cs`

**Step 1: Register services and middleware**

In `Program.cs`, after the existing `builder.Services` registrations (around line 66), add:

```csharp
// API Key storage (SQLite)
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".lmsupply", "api-keys.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContextFactory<ApiKeyDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddSingleton<ApiKeyService>();
```

Required usings at top of file:
```csharp
using LMSupply.Console.Host.Data;
using Microsoft.EntityFrameworkCore;
```

**Step 2: Run EF migration to create the database at startup**

After `var app = builder.Build();`, add:

```csharp
// Ensure database is created and run log cleanup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApiKeyDbContext>>();
    await using var ctx = await db.CreateDbContextAsync();
    await ctx.Database.EnsureCreatedAsync();
}
var apiKeyService = app.Services.GetRequiredService<ApiKeyService>();
await apiKeyService.CleanupOldLogsAsync();
```

**Step 3: Register middleware (after RequestIdMiddleware)**

Change:
```csharp
app.UseMiddleware<RequestIdMiddleware>();
app.UseMiddleware<ErrorMiddleware>();
```
To:
```csharp
app.UseMiddleware<RequestIdMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<ErrorMiddleware>();
```

**Step 4: Register endpoints (after existing MapModelRegistryEndpoints)**

Add:
```csharp
app.MapApiKeyEndpoints();
```

**Step 5: Build and run a quick smoke test**
```bash
dotnet build console/host/LMSupply.Console.Host.csproj
```

Then start the server and verify:
```bash
# Should return empty array
curl http://localhost:5000/api/keys

# Create a key
curl -X POST http://localhost:5000/api/keys \
  -H "Content-Type: application/json" \
  -d '{"name":"test"}'

# After creating a key, unauthed request should be 401
curl http://localhost:5000/v1/embeddings
```

**Step 6: Commit**
```bash
git add console/host/Program.cs
git commit -m "feat(console): wire up API key middleware and endpoints in Program.cs"
```

---

## Task 7: UI — API types and client

**Files:**
- Modify: `console/ui/src/api/types.ts`
- Modify: `console/ui/src/api/client.ts`

**Step 1: Add types to `console/ui/src/api/types.ts`**

Append at end of file:
```typescript
// ─── API Keys ────────────────────────────────────────────────────────────────

export interface ApiKeyResponse {
  id: string;
  name: string;
  keyPrefix: string;
  createdAt: string;
  lastUsedAt: string | null;
  totalRequests: number;
}

export interface ApiKeyCreatedResponse {
  id: string;
  name: string;
  key: string;          // full key — shown once
  keyPrefix: string;
  createdAt: string;
}

export interface RequestsByDay {
  date: string;
  count: number;
}

export interface RequestsByEndpoint {
  path: string;
  count: number;
}

export interface ApiKeyStats {
  totalRequests: number;
  errorRate: number;
  avgDurationMs: number;
  requestsByDay: RequestsByDay[];
  requestsByEndpoint: RequestsByEndpoint[];
}
```

**Step 2: Add API calls to `console/ui/src/api/client.ts`**

Import the new types (add to existing imports):
```typescript
import type { ApiKeyResponse, ApiKeyCreatedResponse, ApiKeyStats } from './types';
```

Then append API methods. Find where other `export const api = { ... }` methods are defined and add inside the object:
```typescript
  // ─── API Keys ──────────────────────────────────────────────────────────────
  listApiKeys: async (): Promise<ApiKeyResponse[]> => {
    const res = await fetch(`${API_BASE}/keys`);
    if (!res.ok) throw new Error('Failed to list API keys');
    return res.json();
  },

  createApiKey: async (name: string): Promise<ApiKeyCreatedResponse> => {
    const res = await fetch(`${API_BASE}/keys`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name }),
    });
    if (!res.ok) throw new Error('Failed to create API key');
    return res.json();
  },

  deleteApiKey: async (id: string): Promise<void> => {
    const res = await fetch(`${API_BASE}/keys/${id}`, { method: 'DELETE' });
    if (!res.ok) throw new Error('Failed to delete API key');
  },

  getApiKeyStats: async (days = 7): Promise<ApiKeyStats> => {
    const res = await fetch(`${API_BASE}/keys/stats?days=${days}`);
    if (!res.ok) throw new Error('Failed to fetch API key stats');
    return res.json();
  },
```

**Step 3: Verify UI builds**
```bash
cd console/ui && npm run build
```
Expected: No TypeScript errors.

**Step 4: Commit**
```bash
git add console/ui/src/api/types.ts console/ui/src/api/client.ts
git commit -m "feat(console/ui): add API key types and client methods"
```

---

## Task 8: UI — Zustand store

**Files:**
- Create: `console/ui/src/stores/apiKeyStore.ts`

**Step 1: Create `console/ui/src/stores/apiKeyStore.ts`**

```typescript
import { create } from 'zustand';
import type { ApiKeyResponse, ApiKeyCreatedResponse, ApiKeyStats } from '../api/types';
import { api } from '../api/client';

interface ApiKeyState {
  keys: ApiKeyResponse[];
  stats: ApiKeyStats | null;
  statsDays: number;
  isLoading: boolean;
  error: string | null;

  fetchKeys: () => Promise<void>;
  createKey: (name: string) => Promise<ApiKeyCreatedResponse>;
  deleteKey: (id: string) => Promise<void>;
  fetchStats: (days?: number) => Promise<void>;
}

export const useApiKeyStore = create<ApiKeyState>((set, get) => ({
  keys: [],
  stats: null,
  statsDays: 7,
  isLoading: false,
  error: null,

  fetchKeys: async () => {
    set({ isLoading: true, error: null });
    try {
      const keys = await api.listApiKeys();
      set({ keys, isLoading: false });
    } catch (e) {
      set({ error: String(e), isLoading: false });
    }
  },

  createKey: async (name: string) => {
    const created = await api.createApiKey(name);
    await get().fetchKeys(); // refresh list
    return created;
  },

  deleteKey: async (id: string) => {
    await api.deleteApiKey(id);
    set(s => ({ keys: s.keys.filter(k => k.id !== id) }));
  },

  fetchStats: async (days = 7) => {
    set({ statsDays: days });
    try {
      const stats = await api.getApiKeyStats(days);
      set({ stats });
    } catch {
      // stats failure is non-critical
    }
  },
}));
```

**Step 2: Commit**
```bash
git add console/ui/src/stores/apiKeyStore.ts
git commit -m "feat(console/ui): add apiKeyStore with Zustand"
```

---

## Task 9: UI — ApiKeys page

**Files:**
- Create: `console/ui/src/pages/ApiKeys.tsx`

**Step 1: Create `console/ui/src/pages/ApiKeys.tsx`**

```tsx
import { useEffect, useState } from 'react';
import { Key, Plus, Trash2, Copy, Check, AlertTriangle, ShieldCheck } from 'lucide-react';
import { useApiKeyStore } from '../stores/apiKeyStore';
import type { ApiKeyCreatedResponse } from '../api/types';

// ─── Create Key Dialog ────────────────────────────────────────────────────────

function CreateKeyDialog({ onClose, onCreated }: {
  onClose: () => void;
  onCreated: (result: ApiKeyCreatedResponse) => void;
}) {
  const [name, setName] = useState('');
  const [isCreating, setIsCreating] = useState(false);
  const { createKey } = useApiKeyStore();

  const handleCreate = async () => {
    if (!name.trim()) return;
    setIsCreating(true);
    try {
      const result = await createKey(name.trim());
      onCreated(result);
    } finally {
      setIsCreating(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="bg-card border border-border rounded-lg p-6 w-full max-w-md shadow-lg">
        <h2 className="text-lg font-semibold mb-4">Create API Key</h2>
        <label className="block text-sm font-medium mb-1">Name</label>
        <input
          className="w-full border border-border rounded-md px-3 py-2 text-sm bg-background mb-4 focus:outline-none focus:ring-2 focus:ring-primary"
          placeholder="e.g., My App"
          value={name}
          onChange={e => setName(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && handleCreate()}
          autoFocus
        />
        <div className="flex justify-end gap-2">
          <button onClick={onClose} className="px-4 py-2 text-sm rounded-md hover:bg-accent">Cancel</button>
          <button
            onClick={handleCreate}
            disabled={!name.trim() || isCreating}
            className="px-4 py-2 text-sm font-medium rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
          >
            {isCreating ? 'Creating...' : 'Create'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ─── Show Key Dialog (one-time reveal) ───────────────────────────────────────

function ShowKeyDialog({ result, onClose }: { result: ApiKeyCreatedResponse; onClose: () => void }) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    await navigator.clipboard.writeText(result.key);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="bg-card border border-border rounded-lg p-6 w-full max-w-lg shadow-lg">
        <h2 className="text-lg font-semibold mb-1">API Key Created</h2>
        <p className="text-sm text-amber-500 flex items-center gap-1 mb-4">
          <AlertTriangle className="w-4 h-4" />
          Copy this key now. It will not be shown again.
        </p>
        <div className="flex items-center gap-2 bg-muted rounded-md px-3 py-2 font-mono text-sm mb-4 break-all">
          <span className="flex-1 select-all">{result.key}</span>
          <button onClick={copy} className="shrink-0 p-1 rounded hover:bg-accent" title="Copy">
            {copied ? <Check className="w-4 h-4 text-green-500" /> : <Copy className="w-4 h-4" />}
          </button>
        </div>
        <div className="flex justify-end">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium rounded-md bg-primary text-primary-foreground hover:bg-primary/90"
          >
            Done
          </button>
        </div>
      </div>
    </div>
  );
}

// ─── Stats Panel ──────────────────────────────────────────────────────────────

function StatsPanel() {
  const { stats, statsDays, fetchStats } = useApiKeyStore();

  useEffect(() => { fetchStats(statsDays); }, [statsDays]);

  const dayOptions = [1, 7, 30];

  return (
    <div className="bg-card border border-border rounded-lg p-4">
      <div className="flex items-center justify-between mb-3">
        <h3 className="text-sm font-semibold">Request Statistics</h3>
        <div className="flex gap-1">
          {dayOptions.map(d => (
            <button
              key={d}
              onClick={() => fetchStats(d)}
              className={`px-2 py-0.5 text-xs rounded ${statsDays === d ? 'bg-primary text-primary-foreground' : 'hover:bg-accent'}`}
            >
              {d}d
            </button>
          ))}
        </div>
      </div>
      {stats ? (
        <div className="space-y-3">
          <div className="grid grid-cols-3 gap-3 text-center">
            <div>
              <p className="text-2xl font-bold">{stats.totalRequests}</p>
              <p className="text-xs text-muted-foreground">Total</p>
            </div>
            <div>
              <p className="text-2xl font-bold">{(stats.errorRate * 100).toFixed(1)}%</p>
              <p className="text-xs text-muted-foreground">Error Rate</p>
            </div>
            <div>
              <p className="text-2xl font-bold">{Math.round(stats.avgDurationMs)}ms</p>
              <p className="text-xs text-muted-foreground">Avg Latency</p>
            </div>
          </div>
          {stats.requestsByEndpoint.length > 0 && (
            <div>
              <p className="text-xs font-medium text-muted-foreground mb-1">Top Endpoints</p>
              <div className="space-y-1">
                {stats.requestsByEndpoint.map(e => (
                  <div key={e.path} className="flex items-center gap-2 text-xs">
                    <span className="flex-1 font-mono truncate text-muted-foreground">{e.path}</span>
                    <span className="font-medium">{e.count}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No data</p>
      )}
    </div>
  );
}

// ─── Main Page ────────────────────────────────────────────────────────────────

export function ApiKeys() {
  const { keys, isLoading, fetchKeys, deleteKey } = useApiKeyStore();
  const [showCreate, setShowCreate] = useState(false);
  const [createdKey, setCreatedKey] = useState<ApiKeyCreatedResponse | null>(null);

  useEffect(() => {
    fetchKeys();
  }, [fetchKeys]);

  const handleDelete = async (id: string, name: string) => {
    if (!confirm(`Delete API key "${name}"? This cannot be undone.`)) return;
    await deleteKey(id);
  };

  const handleCreated = (result: ApiKeyCreatedResponse) => {
    setShowCreate(false);
    setCreatedKey(result);
  };

  const formatDate = (iso: string) =>
    new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });

  return (
    <div className="p-6 max-w-4xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Key className="w-5 h-5" />
          <h1 className="text-xl font-semibold">API Keys</h1>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          className="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium rounded-md bg-primary text-primary-foreground hover:bg-primary/90"
        >
          <Plus className="w-4 h-4" />
          Create Key
        </button>
      </div>

      {/* Status banner */}
      {keys.length === 0 ? (
        <div className="flex items-center gap-2 px-4 py-3 rounded-lg border border-amber-200 bg-amber-50 text-amber-800 text-sm">
          <AlertTriangle className="w-4 h-4 shrink-0" />
          No API keys — all requests are allowed without authentication. Create a key to enable access control.
        </div>
      ) : (
        <div className="flex items-center gap-2 px-4 py-3 rounded-lg border border-green-200 bg-green-50 text-green-800 text-sm">
          <ShieldCheck className="w-4 h-4 shrink-0" />
          {keys.length} API key{keys.length > 1 ? 's' : ''} active — all requests require a valid Bearer token.
        </div>
      )}

      {/* Keys table */}
      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading...</p>
      ) : keys.length > 0 ? (
        <div className="border border-border rounded-lg overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-muted-foreground">
              <tr>
                <th className="text-left px-4 py-2 font-medium">Name</th>
                <th className="text-left px-4 py-2 font-medium">Key</th>
                <th className="text-left px-4 py-2 font-medium">Created</th>
                <th className="text-left px-4 py-2 font-medium">Last Used</th>
                <th className="text-right px-4 py-2 font-medium">Requests</th>
                <th className="px-4 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {keys.map(k => (
                <tr key={k.id} className="hover:bg-muted/30">
                  <td className="px-4 py-3 font-medium">{k.name}</td>
                  <td className="px-4 py-3 font-mono text-muted-foreground">{k.keyPrefix}****</td>
                  <td className="px-4 py-3 text-muted-foreground">{formatDate(k.createdAt)}</td>
                  <td className="px-4 py-3 text-muted-foreground">
                    {k.lastUsedAt ? formatDate(k.lastUsedAt) : '—'}
                  </td>
                  <td className="px-4 py-3 text-right">{k.totalRequests.toLocaleString()}</td>
                  <td className="px-4 py-3 text-right">
                    <button
                      onClick={() => handleDelete(k.id, k.name)}
                      className="p-1.5 rounded hover:bg-destructive/10 text-destructive"
                      title="Delete key"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}

      {/* Stats */}
      {keys.length > 0 && <StatsPanel />}

      {/* Dialogs */}
      {showCreate && <CreateKeyDialog onClose={() => setShowCreate(false)} onCreated={handleCreated} />}
      {createdKey && <ShowKeyDialog result={createdKey} onClose={() => setCreatedKey(null)} />}
    </div>
  );
}
```

**Step 2: Commit**
```bash
git add console/ui/src/pages/ApiKeys.tsx
git commit -m "feat(console/ui): add ApiKeys page with key management and stats"
```

---

## Task 10: Wire up UI routing and sidebar

**Files:**
- Modify: `console/ui/src/pages/index.ts` (or wherever pages are re-exported)
- Modify: `console/ui/src/App.tsx`
- Modify: `console/ui/src/components/Layout.tsx`

**Step 1: Check pages index**

Look for `console/ui/src/pages/index.ts`. If it exists, add:
```typescript
export { ApiKeys } from './ApiKeys';
```
If no index file — pages are imported directly in `App.tsx` (that's fine, import directly).

**Step 2: Add route in `App.tsx`**

Add import:
```tsx
import { ApiKeys } from './pages/ApiKeys'; // or from './pages' if index exists
```

Add route inside `<Route element={<Layout />}>`:
```tsx
<Route path="/api-keys" element={<ApiKeys />} />
```

**Step 3: Add sidebar nav item in `Layout.tsx`**

Add to imports:
```tsx
import { Key } from 'lucide-react';
```

In `SidebarContent`, in the bottom section (after the Models NavLink, before API Docs), add:
```tsx
<NavLink
  to="/api-keys"
  onClick={onNavigate}
  className={({ isActive }) =>
    cn(
      'flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium transition-colors',
      isActive
        ? 'bg-primary text-primary-foreground'
        : 'hover:bg-accent hover:text-accent-foreground'
    )
  }
>
  <Key className="w-4 h-4" />
  API Keys
</NavLink>
```

**Step 4: Build UI**
```bash
cd console/ui && npm run build
```
Expected: Build succeeded, no TypeScript errors.

**Step 5: Commit**
```bash
git add console/ui/src/App.tsx console/ui/src/components/Layout.tsx
git commit -m "feat(console/ui): add API Keys route and sidebar nav item"
```

---

## Task 11: Swagger Bearer auth documentation

**Files:**
- Modify: `console/host/Program.cs`

**Step 1: Update Swagger config in Program.cs**

Change the Swagger setup from:
```csharp
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "LMSupply Console API", Version = "v1" });
});
```
To:
```csharp
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "LMSupply Console API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "API Key",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your API key: lms-...",
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            []
        }
    });
});
```

**Step 2: Build and smoke test**
```bash
dotnet build console/host/LMSupply.Console.Host.csproj
dotnet run --project console/host/LMSupply.Console.Host.csproj
```
Visit `http://localhost:5000/swagger` — should show "Authorize" button.

**Step 3: Commit**
```bash
git add console/host/Program.cs
git commit -m "feat(console): add Bearer auth documentation to Swagger UI"
```

---

## Task 12: End-to-end smoke test and push

**Step 1: Build everything**
```bash
dotnet build
cd console/ui && npm run build
```

**Step 2: Manual test flow**
1. Start server: `dotnet run --project console/host/LMSupply.Console.Host.csproj`
2. Open `http://localhost:5000` → navigate to API Keys
3. Confirm "No API keys" banner is yellow
4. Create a key → copy the full key from the dialog
5. Confirm the list shows the key with `lms-...****` prefix
6. Test unauthenticated request → expect 401:
   ```bash
   curl -s http://localhost:5000/v1/embeddings -d '{}' -H "Content-Type: application/json"
   # Expected: {"error":{"message":"No API key provided...","type":"auth_error","code":"invalid_api_key"}}
   ```
7. Test authenticated request → expect non-401:
   ```bash
   curl -s http://localhost:5000/v1/embeddings \
     -H "Authorization: Bearer lms-<your-key>" \
     -H "Content-Type: application/json" \
     -d '{"model":"default","input":"hello"}'
   ```
8. Check `/api/keys/stats?days=7` shows the request logged
9. Delete the key → confirm banner goes back to yellow

**Step 3: Push**
```bash
git push
```
