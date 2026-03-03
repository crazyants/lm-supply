namespace LMSupply.Console.Host.Services;

/// <summary>
/// Disposable guard that calls BeginUse on construction and EndUse on dispose.
/// Does NOT dispose the model itself — that's managed by ModelManagerService.
/// </summary>
public sealed class ModelScope<T> : IAsyncDisposable where T : IAsyncDisposable
{
    private readonly ModelManagerService _manager;
    private readonly string _key;
    private bool _disposed;

    public T Model { get; }

    internal ModelScope(ModelManagerService manager, string key, T model)
    {
        _manager = manager;
        _key = key;
        Model = model;
        manager.BeginUse(key);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _manager.EndUse(_key);
        }
        return ValueTask.CompletedTask;
    }
}
