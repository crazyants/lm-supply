using System.Collections.Concurrent;
using System.Diagnostics;
using LMSupply.Download;

namespace LMSupply.Llama.Server;

/// <summary>
/// Pool for managing llama-server instances.
/// Reuses servers for the same model/backend/context configuration.
/// Automatically cleans up on process exit.
/// </summary>
public sealed class LlamaServerPool : IAsyncDisposable
{
    private static readonly Lazy<LlamaServerPool> _instance = new(
        () =>
        {
            var pool = new LlamaServerPool();
            RegisterForCleanup(pool);
            return pool;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static bool _cleanupRegistered;

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static LlamaServerPool Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, PooledServer> _servers = new();
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    private static void RegisterForCleanup(LlamaServerPool pool)
    {
        if (_cleanupRegistered)
            return;

        _cleanupRegistered = true;

        // Register for process exit cleanup
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // Synchronous cleanup on process exit
            pool.DisposeAsync().AsTask().GetAwaiter().GetResult();
        };

        // Also register for console cancel (Ctrl+C)
        try
        {
            Console.CancelKeyPress += (_, e) =>
            {
                // Allow cancellation to proceed after cleanup
                e.Cancel = false;
                pool.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"[LlamaServerPool] Console handler registration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Disposes the singleton instance and all pooled servers.
    /// Call this for explicit cleanup before application exit.
    /// </summary>
    public static async ValueTask DisposeInstanceAsync()
    {
        if (_instance.IsValueCreated)
        {
            await _instance.Value.DisposeAsync();
        }
    }

    /// <summary>
    /// Options for the server pool.
    /// </summary>
    public LlamaServerPoolOptions Options { get; }

    /// <summary>
    /// Creates a new server pool with default options.
    /// </summary>
    public LlamaServerPool() : this(new LlamaServerPoolOptions())
    {
    }

    /// <summary>
    /// Creates a new server pool with custom options.
    /// </summary>
    public LlamaServerPool(LlamaServerPoolOptions options)
    {
        Options = options;

        // Start cleanup timer
        _cleanupTimer = new Timer(
            CleanupIdleServers,
            null,
            options.CleanupInterval,
            options.CleanupInterval);
    }

    /// <summary>
    /// Leases a server for the specified configuration.
    /// Returns an existing server if available, or creates a new one.
    /// </summary>
    public async Task<ServerLease> LeaseAsync(
        string serverPath,
        LlamaServerConfig config,
        LlamaServerBackend backend,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var key = MakeKey(config.ModelPath, backend, config.ContextSize, config.Mode);

        // Try to get existing server
        if (_servers.TryGetValue(key, out var pooledServer))
        {
            if (pooledServer.TryLease())
            {
                return new ServerLease(pooledServer, this);
            }

            // Server is busy or dead, try to create new one
        }

        // Create new server
        await _createLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_servers.TryGetValue(key, out pooledServer))
            {
                if (pooledServer.TryLease())
                {
                    return new ServerLease(pooledServer, this);
                }
            }

            // Check server limit
            var activeCount = _servers.Values.Count(s => s.IsAlive);
            if (activeCount >= Options.MaxServers)
            {
                // Evict oldest idle server
                await EvictOldestIdleServerAsync();
            }

            // Start new server
            progress?.Report(new DownloadProgress
            {
                FileName = Path.GetFileName(config.ModelPath),
                Phase = DownloadPhase.Extracting
            });

            var serverProcess = await LlamaServerProcess.StartAsync(
                serverPath,
                config,
                backend,
                cancellationToken);

            var client = new LlamaServerClient(serverProcess.Info!.BaseUrl, maxContextLength: config.ContextSize);

            var newPooledServer = new PooledServer(key, serverProcess, client, config.ModelPath, backend);
            newPooledServer.TryLease();

            _servers[key] = newPooledServer;

            return new ServerLease(newPooledServer, this);
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <summary>
    /// Returns a server to the pool.
    /// </summary>
    internal static void Release(PooledServer server)
    {
        server.Release();
    }

    /// <summary>
    /// Gets the current pool status.
    /// </summary>
    public PoolStatus GetStatus()
    {
        var servers = _servers.Values.ToList();

        return new PoolStatus
        {
            TotalServers = servers.Count,
            ActiveServers = servers.Count(s => s.IsInUse),
            IdleServers = servers.Count(s => s.IsAlive && !s.IsInUse),
            Entries = servers.Select(s => new PoolEntry
            {
                Key = s.Key,
                ModelPath = s.ModelPath,
                Backend = s.Backend,
                IsInUse = s.IsInUse,
                LastUsed = s.LastUsed,
                ProcessId = s.Server.Info?.ProcessId ?? 0
            }).ToList()
        };
    }

    private async Task EvictOldestIdleServerAsync()
    {
        var oldestIdle = _servers.Values
            .Where(s => s.IsAlive && !s.IsInUse)
            .OrderBy(s => s.LastUsed)
            .FirstOrDefault();

        if (oldestIdle != null && _servers.TryRemove(oldestIdle.Key, out var removed))
        {
            await removed.DisposeAsync();
        }
    }

    private async void CleanupIdleServers(object? state)
    {
        if (_disposed)
            return;

        var now = DateTimeOffset.UtcNow;
        var serversToRemove = _servers.Values
            .Where(s => !s.IsInUse && (now - s.LastUsed) > Options.IdleTimeout)
            .ToList();

        foreach (var server in serversToRemove)
        {
            if (_servers.TryRemove(server.Key, out var removed))
            {
                await removed.DisposeAsync().ConfigureAwait(false);
            }
        }

        // Also remove dead servers
        var deadServers = _servers.Values.Where(s => !s.IsAlive).ToList();
        foreach (var server in deadServers)
        {
            if (_servers.TryRemove(server.Key, out var removed))
            {
                await removed.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static string MakeKey(string modelPath, LlamaServerBackend backend, int contextSize, ServerMode mode = ServerMode.Generation)
        => $"{modelPath}|{backend}|{contextSize}|{mode}";

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _cleanupTimer.DisposeAsync();
        _createLock.Dispose();

        foreach (var server in _servers.Values)
        {
            await server.DisposeAsync();
        }

        _servers.Clear();
    }
}

/// <summary>
/// Options for the server pool.
/// </summary>
public sealed class LlamaServerPoolOptions
{
    /// <summary>
    /// Maximum number of servers to keep in the pool.
    /// Default: 3.
    /// </summary>
    public int MaxServers { get; set; } = 3;

    /// <summary>
    /// Time after which an idle server is removed from the pool.
    /// Default: 10 minutes.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Interval for cleanup of idle servers.
    /// Default: 1 minute.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// A pooled llama-server instance.
/// </summary>
internal sealed class PooledServer : IAsyncDisposable
{
    private int _leaseCount;
    private bool _disposed;

    public string Key { get; }
    public LlamaServerProcess Server { get; }
    public LlamaServerClient Client { get; }
    public string ModelPath { get; }
    public LlamaServerBackend Backend { get; }
    public DateTimeOffset LastUsed { get; private set; }

    public bool IsAlive => !_disposed && Server.IsRunning;
    public bool IsInUse => _leaseCount > 0;

    public PooledServer(
        string key,
        LlamaServerProcess server,
        LlamaServerClient client,
        string modelPath,
        LlamaServerBackend backend)
    {
        Key = key;
        Server = server;
        Client = client;
        ModelPath = modelPath;
        Backend = backend;
        LastUsed = DateTimeOffset.UtcNow;
    }

    public bool TryLease()
    {
        if (_disposed || !Server.IsRunning)
            return false;

        Interlocked.Increment(ref _leaseCount);
        LastUsed = DateTimeOffset.UtcNow;
        return true;
    }

    public void Release()
    {
        Interlocked.Decrement(ref _leaseCount);
        LastUsed = DateTimeOffset.UtcNow;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        Client.Dispose();
        await Server.DisposeAsync();
    }
}

/// <summary>
/// A lease for a pooled server.
/// Disposing the lease returns the server to the pool.
/// </summary>
public sealed class ServerLease : IAsyncDisposable
{
    private readonly PooledServer _server;
    private readonly LlamaServerPool _pool;
    private bool _disposed;

    internal ServerLease(PooledServer server, LlamaServerPool pool)
    {
        _server = server;
        _pool = pool;
    }

    /// <summary>
    /// Gets the server process.
    /// </summary>
    public LlamaServerProcess Server => _server.Server;

    /// <summary>
    /// Gets the HTTP client for the server.
    /// </summary>
    public LlamaServerClient Client => _server.Client;

    /// <summary>
    /// Gets the backend being used.
    /// </summary>
    public LlamaServerBackend Backend => _server.Backend;

    /// <summary>
    /// Returns the server to the pool.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        LlamaServerPool.Release(_server);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Status of the server pool.
/// </summary>
public sealed record PoolStatus
{
    public int TotalServers { get; init; }
    public int ActiveServers { get; init; }
    public int IdleServers { get; init; }
    public IReadOnlyList<PoolEntry> Entries { get; init; } = [];
}

/// <summary>
/// Information about a pooled server.
/// </summary>
public sealed record PoolEntry
{
    public required string Key { get; init; }
    public required string ModelPath { get; init; }
    public required LlamaServerBackend Backend { get; init; }
    public bool IsInUse { get; init; }
    public DateTimeOffset LastUsed { get; init; }
    public int ProcessId { get; init; }
}
