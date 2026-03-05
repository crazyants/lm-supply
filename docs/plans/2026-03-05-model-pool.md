# ModelPool Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 모든 도메인에서 named load/unload/조회를 지원하는 제네릭 `ModelPool<TModel, TOptions>` 추상화를 Core에 추가하고, `GeneratorPool`을 이 위에서 동작하도록 리팩터링한다.

**Architecture:** `IModelLoader<TModel, TOptions>` 인터페이스를 각 도메인이 구현하고, 제네릭 `ModelPool<TModel, TOptions>`가 LRU eviction + 메모리 안전 마진을 중앙 관리한다. `GeneratorPool`은 기존 API를 유지하면서 내부적으로 새 `ModelPool<>`에 위임한다.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, `LMSupply.Core.Hardware.HardwareProfile`

---

### Task 1: Core에 `ModelPoolOptions` 및 `LoadedModelInfo` 이동

Generator 패키지의 `GeneratorPool.cs`에 있는 `GeneratorPoolOptions`와 `LoadedModelInfo`를 Core로 이동한다.

**Files:**
- Create: `src/LMSupply.Core/Pool/ModelPoolOptions.cs`
- Create: `src/LMSupply.Core/Pool/LoadedModelInfo.cs`

**Step 1: 실패 테스트 작성**

파일: `tests/LMSupply.Core.Tests/Pool/ModelPoolOptionsTests.cs`

```csharp
using FluentAssertions;
using LMSupply.Pool;

namespace LMSupply.Core.Tests.Pool;

public class ModelPoolOptionsTests
{
    [Fact]
    public void DefaultOptions_HasExpectedValues()
    {
        var opts = new ModelPoolOptions();

        opts.MaxMemoryBytes.Should().BeNull();
        opts.MemorySafetyMargin.Should().Be(0.2);
        opts.MaxLoadedModels.Should().Be(2);
    }
}
```

**Step 2: 테스트 실패 확인**

```bash
dotnet test tests/LMSupply.Core.Tests --filter "ModelPoolOptionsTests" -v
```

예상: FAIL — `LMSupply.Pool` 네임스페이스 없음

**Step 3: `ModelPoolOptions.cs` 작성**

```csharp
namespace LMSupply.Pool;

/// <summary>
/// Configuration options for the model pool.
/// </summary>
public sealed class ModelPoolOptions
{
    /// <summary>
    /// Maximum memory to allocate for models (in bytes).
    /// If null, uses available GPU or system memory.
    /// </summary>
    public long? MaxMemoryBytes { get; set; }

    /// <summary>
    /// Safety margin for memory calculations (0.0-1.0).
    /// Defaults to 0.2 (20% buffer).
    /// </summary>
    public double MemorySafetyMargin { get; set; } = 0.2;

    /// <summary>
    /// Maximum number of models to keep loaded simultaneously.
    /// Defaults to 2.
    /// </summary>
    public int MaxLoadedModels { get; set; } = 2;
}
```

파일: `src/LMSupply.Core/Pool/ModelPoolOptions.cs`

**Step 4: `LoadedModelInfo.cs` 작성**

```csharp
namespace LMSupply.Pool;

/// <summary>
/// Information about a loaded model in the pool.
/// </summary>
/// <param name="ModelId">The model identifier.</param>
/// <param name="AllocatedMemoryBytes">Memory allocated for this model.</param>
/// <param name="LoadedAt">When the model was loaded.</param>
/// <param name="LastAccessedAt">When the model was last accessed.</param>
public sealed record LoadedModelInfo(
    string ModelId,
    long AllocatedMemoryBytes,
    DateTime LoadedAt,
    DateTime LastAccessedAt);
```

파일: `src/LMSupply.Core/Pool/LoadedModelInfo.cs`

**Step 5: 테스트 통과 확인**

```bash
dotnet test tests/LMSupply.Core.Tests --filter "ModelPoolOptionsTests" -v
```

예상: PASS

**Step 6: 커밋**

```bash
git add src/LMSupply.Core/Pool/ tests/LMSupply.Core.Tests/Pool/
git commit -m "feat(core): add ModelPoolOptions and LoadedModelInfo to Core/Pool"
```

---

### Task 2: Core에 `IModelLoader<TModel, TOptions>` 추가

**Files:**
- Create: `src/LMSupply.Core/Pool/IModelLoader.cs`

**Step 1: 실패 테스트 작성**

파일: `tests/LMSupply.Core.Tests/Pool/IModelLoaderTests.cs`

```csharp
using FluentAssertions;
using LMSupply.Pool;

namespace LMSupply.Core.Tests.Pool;

public class IModelLoaderTests
{
    // Minimal fake model and loader to verify the interface compiles and behaves correctly
    private sealed class FakeModel : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return default; }
    }

    private sealed class FakeLoader : IModelLoader<FakeModel, object>
    {
        public Task<FakeModel> LoadAsync(string modelId, object? options, CancellationToken ct)
            => Task.FromResult(new FakeModel());

        public long EstimateMemoryBytes(string modelId, object? options) => 100_000_000;
    }

    [Fact]
    public async Task FakeLoader_LoadAsync_ReturnsModel()
    {
        IModelLoader<FakeModel, object> loader = new FakeLoader();

        var model = await loader.LoadAsync("test", null, CancellationToken.None);

        model.Should().NotBeNull();
    }

    [Fact]
    public void FakeLoader_EstimateMemoryBytes_Returns100MB()
    {
        IModelLoader<FakeModel, object> loader = new FakeLoader();

        var bytes = loader.EstimateMemoryBytes("test", null);

        bytes.Should().Be(100_000_000);
    }
}
```

**Step 2: 테스트 실패 확인**

```bash
dotnet test tests/LMSupply.Core.Tests --filter "IModelLoaderTests" -v
```

예상: FAIL — `IModelLoader` 없음

**Step 3: `IModelLoader.cs` 작성**

```csharp
namespace LMSupply.Pool;

/// <summary>
/// Defines how a domain loads and estimates memory for a model.
/// Each domain implements this interface internally.
/// </summary>
/// <typeparam name="TModel">The model type (must implement IAsyncDisposable).</typeparam>
/// <typeparam name="TOptions">The options type for loading.</typeparam>
public interface IModelLoader<TModel, TOptions>
    where TModel : IAsyncDisposable
    where TOptions : class
{
    /// <summary>
    /// Loads a model by ID with the specified options.
    /// </summary>
    Task<TModel> LoadAsync(string modelId, TOptions? options, CancellationToken cancellationToken);

    /// <summary>
    /// Estimates the memory requirement in bytes for loading this model.
    /// Return a reasonable domain-specific default if unknown.
    /// </summary>
    long EstimateMemoryBytes(string modelId, TOptions? options);
}
```

파일: `src/LMSupply.Core/Pool/IModelLoader.cs`

**Step 4: 테스트 통과 확인**

```bash
dotnet test tests/LMSupply.Core.Tests --filter "IModelLoaderTests" -v
```

예상: PASS

**Step 5: 커밋**

```bash
git add src/LMSupply.Core/Pool/IModelLoader.cs tests/LMSupply.Core.Tests/Pool/IModelLoaderTests.cs
git commit -m "feat(core): add IModelLoader interface to Core/Pool"
```

---

### Task 3: Core에 `ModelPool<TModel, TOptions>` 구현

`GeneratorPool`의 LRU + 메모리 안전 마진 로직을 제네릭화하여 Core에 추가한다.

**Files:**
- Create: `src/LMSupply.Core/Pool/ModelPool.cs`
- Create: `tests/LMSupply.Core.Tests/Pool/ModelPoolTests.cs`

**Step 1: 실패 테스트 작성**

파일: `tests/LMSupply.Core.Tests/Pool/ModelPoolTests.cs`

```csharp
using FluentAssertions;
using LMSupply.Pool;

namespace LMSupply.Core.Tests.Pool;

public class ModelPoolTests
{
    private sealed class FakeModel : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return default; }
    }

    private sealed class FakeLoader : IModelLoader<FakeModel, object>
    {
        public int LoadCallCount { get; private set; }

        public Task<FakeModel> LoadAsync(string modelId, object? options, CancellationToken ct)
        {
            LoadCallCount++;
            return Task.FromResult(new FakeModel());
        }

        public long EstimateMemoryBytes(string modelId, object? options) => 100_000_000; // 100 MB
    }

    [Fact]
    public async Task GetOrLoadAsync_FirstCall_LoadsModel()
    {
        var loader = new FakeLoader();
        await using var pool = new ModelPool<FakeModel, object>(loader,
            new ModelPoolOptions { MaxMemoryBytes = 2_000_000_000 });

        var model = await pool.GetOrLoadAsync("m1");

        model.Should().NotBeNull();
        loader.LoadCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrLoadAsync_SameModelTwice_LoadsOnlyOnce()
    {
        var loader = new FakeLoader();
        await using var pool = new ModelPool<FakeModel, object>(loader,
            new ModelPoolOptions { MaxMemoryBytes = 2_000_000_000 });

        var m1 = await pool.GetOrLoadAsync("m1");
        var m2 = await pool.GetOrLoadAsync("m1");

        m1.Should().BeSameAs(m2);
        loader.LoadCallCount.Should().Be(1);
    }

    [Fact]
    public async Task IsLoaded_AfterLoad_ReturnsTrue()
    {
        var loader = new FakeLoader();
        await using var pool = new ModelPool<FakeModel, object>(loader,
            new ModelPoolOptions { MaxMemoryBytes = 2_000_000_000 });

        await pool.GetOrLoadAsync("m1");

        pool.IsLoaded("m1").Should().BeTrue();
        pool.IsLoaded("m2").Should().BeFalse();
    }

    [Fact]
    public async Task UnloadAsync_RemovesModel()
    {
        var loader = new FakeLoader();
        await using var pool = new ModelPool<FakeModel, object>(loader,
            new ModelPoolOptions { MaxMemoryBytes = 2_000_000_000 });

        await pool.GetOrLoadAsync("m1");
        await pool.UnloadAsync("m1");

        pool.IsLoaded("m1").Should().BeFalse();
        pool.LoadedModelCount.Should().Be(0);
    }

    [Fact]
    public async Task UnloadAllAsync_ClearsPool()
    {
        var loader = new FakeLoader();
        await using var pool = new ModelPool<FakeModel, object>(loader,
            new ModelPoolOptions { MaxMemoryBytes = 2_000_000_000 });

        await pool.GetOrLoadAsync("m1");
        await pool.GetOrLoadAsync("m2");
        await pool.UnloadAllAsync();

        pool.LoadedModelCount.Should().Be(0);
    }

    [Fact]
    public async Task GetLoadedModels_ReturnsAllLoaded()
    {
        var loader = new FakeLoader();
        await using var pool = new ModelPool<FakeModel, object>(loader,
            new ModelPoolOptions { MaxMemoryBytes = 2_000_000_000 });

        await pool.GetOrLoadAsync("m1");
        await pool.GetOrLoadAsync("m2");

        var loaded = pool.GetLoadedModels();
        loaded.Should().HaveCount(2);
        loaded.Select(m => m.ModelId).Should().BeEquivalentTo(["m1", "m2"]);
    }

    [Fact]
    public async Task GetOrLoadAsync_InsufficientMemory_ThrowsInvalidOperation()
    {
        var loader = new FakeLoader(); // estimates 100 MB per model
        await using var pool = new ModelPool<FakeModel, object>(loader,
            new ModelPoolOptions { MaxMemoryBytes = 50_000_000 }); // only 50 MB

        var act = async () => await pool.GetOrLoadAsync("m1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Insufficient memory*");
    }

    [Fact]
    public async Task LruEviction_WhenMemoryFull_EvictsLeastRecentlyUsed()
    {
        var loader = new FakeLoader(); // 100 MB each
        // Allow exactly 2 models (200 MB) with no safety margin
        await using var pool = new ModelPool<FakeModel, object>(loader,
            new ModelPoolOptions { MaxMemoryBytes = 250_000_000, MemorySafetyMargin = 0 });

        var m1 = await pool.GetOrLoadAsync("m1");
        await Task.Delay(5); // ensure different timestamps
        var m2 = await pool.GetOrLoadAsync("m2");
        await Task.Delay(5);

        // m1 was accessed least recently; loading m3 should evict m1
        var m3 = await pool.GetOrLoadAsync("m3");

        pool.IsLoaded("m1").Should().BeFalse("m1 was LRU and should be evicted");
        pool.IsLoaded("m2").Should().BeTrue();
        pool.IsLoaded("m3").Should().BeTrue();
    }
}
```

**Step 2: 테스트 실패 확인**

```bash
dotnet test tests/LMSupply.Core.Tests --filter "ModelPoolTests" -v
```

예상: FAIL — `ModelPool` 없음

**Step 3: `ModelPool.cs` 구현**

```csharp
using System.Collections.Concurrent;
using LMSupply.Hardware;

namespace LMSupply.Pool;

/// <summary>
/// Generic pool for managing multiple model instances with LRU eviction and memory protection.
/// </summary>
/// <typeparam name="TModel">The model type.</typeparam>
/// <typeparam name="TOptions">The options type.</typeparam>
public sealed class ModelPool<TModel, TOptions> : IAsyncDisposable
    where TModel : IAsyncDisposable
    where TOptions : class
{
    private readonly ConcurrentDictionary<string, PooledModel> _models = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly IModelLoader<TModel, TOptions> _loader;
    private readonly ModelPoolOptions _options;
    private readonly long _availableMemory;
    private long _allocatedMemory;
    private bool _disposed;

    /// <summary>
    /// Creates a new model pool with the specified loader and options.
    /// </summary>
    public ModelPool(IModelLoader<TModel, TOptions> loader, ModelPoolOptions? options = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _options = options ?? new ModelPoolOptions();

        var profile = HardwareProfile.Current;
        _availableMemory = _options.MaxMemoryBytes
            ?? profile.GpuInfo.TotalMemoryBytes
            ?? profile.SystemMemoryBytes;
    }

    /// <summary>Gets the number of loaded models.</summary>
    public int LoadedModelCount => _models.Count;

    /// <summary>Gets the total allocated memory across all loaded models.</summary>
    public long AllocatedMemoryBytes => _allocatedMemory;

    /// <summary>Gets the available memory for model loading.</summary>
    public long AvailableMemoryBytes => _availableMemory - _allocatedMemory;

    /// <summary>Gets or loads a model by ID.</summary>
    /// <exception cref="InvalidOperationException">Thrown when memory is insufficient.</exception>
    public async Task<TModel> GetOrLoadAsync(
        string modelId,
        TOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_models.TryGetValue(modelId, out var pooled))
        {
            pooled.UpdateLastAccess();
            return pooled.Model;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_models.TryGetValue(modelId, out pooled))
            {
                pooled.UpdateLastAccess();
                return pooled.Model;
            }

            var memoryRequired = _loader.EstimateMemoryBytes(modelId, options);

            if (!CanAllocate(memoryRequired))
            {
                await EvictModelsAsync(memoryRequired, cancellationToken);

                if (!CanAllocate(memoryRequired))
                {
                    throw new InvalidOperationException(
                        $"Insufficient memory to load model '{modelId}'. " +
                        $"Required: {memoryRequired / (1024.0 * 1024 * 1024):F2} GB, " +
                        $"Available: {AvailableMemoryBytes / (1024.0 * 1024 * 1024):F2} GB");
                }
            }

            var model = await _loader.LoadAsync(modelId, options, cancellationToken);

            pooled = new PooledModel(modelId, model, memoryRequired);
            _models[modelId] = pooled;
            Interlocked.Add(ref _allocatedMemory, memoryRequired);

            return model;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>Checks if a model is currently loaded.</summary>
    public bool IsLoaded(string modelId) => _models.ContainsKey(modelId);

    /// <summary>Unloads a specific model and releases its resources.</summary>
    public async Task UnloadAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_models.TryRemove(modelId, out var pooled))
        {
            Interlocked.Add(ref _allocatedMemory, -pooled.AllocatedMemory);
            await pooled.Model.DisposeAsync();
        }
    }

    /// <summary>Unloads all models and releases resources.</summary>
    public async Task UnloadAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var models = _models.Values.ToList();
        _models.Clear();
        _allocatedMemory = 0;

        foreach (var m in models)
            await m.Model.DisposeAsync();
    }

    /// <summary>Gets information about all loaded models.</summary>
    public IReadOnlyList<LoadedModelInfo> GetLoadedModels() =>
        _models.Values
            .Select(p => new LoadedModelInfo(p.ModelId, p.AllocatedMemory, p.LoadedAt, p.LastAccessedAt))
            .ToList();

    private bool CanAllocate(long requiredBytes)
    {
        var withMargin = (long)(requiredBytes * (1 + _options.MemorySafetyMargin));
        return _allocatedMemory + withMargin <= _availableMemory;
    }

    private async Task EvictModelsAsync(long requiredBytes, CancellationToken cancellationToken)
    {
        var candidates = _models.Values.OrderBy(p => p.LastAccessedAt).ToList();

        foreach (var candidate in candidates)
        {
            if (CanAllocate(requiredBytes))
                break;

            await UnloadAsync(candidate.ModelId, cancellationToken);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        var models = _models.Values.ToList();
        _models.Clear();
        _allocatedMemory = 0;

        foreach (var m in models)
            await m.Model.DisposeAsync();

        _loadLock.Dispose();
    }

    private sealed class PooledModel
    {
        public string ModelId { get; }
        public TModel Model { get; }
        public long AllocatedMemory { get; }
        public DateTime LoadedAt { get; }
        public DateTime LastAccessedAt { get; private set; }

        public PooledModel(string modelId, TModel model, long allocatedMemory)
        {
            ModelId = modelId;
            Model = model;
            AllocatedMemory = allocatedMemory;
            LoadedAt = DateTime.UtcNow;
            LastAccessedAt = DateTime.UtcNow;
        }

        public void UpdateLastAccess() => LastAccessedAt = DateTime.UtcNow;
    }
}
```

파일: `src/LMSupply.Core/Pool/ModelPool.cs`

**Step 4: 테스트 통과 확인**

```bash
dotnet test tests/LMSupply.Core.Tests --filter "ModelPoolTests" -v
```

예상: PASS (8개 테스트 통과)

**Step 5: 전체 빌드 확인**

```bash
dotnet build
```

예상: 0 errors

**Step 6: 커밋**

```bash
git add src/LMSupply.Core/Pool/ModelPool.cs tests/LMSupply.Core.Tests/Pool/ModelPoolTests.cs
git commit -m "feat(core): implement generic ModelPool with LRU eviction and memory safety"
```

---

### Task 4: `GeneratorPool` 리팩터링

`GeneratorPool`을 `ModelPool<IGeneratorModel, GeneratorOptions>` 위임 구조로 교체하고, Core의 `LoadedModelInfo`와 `ModelPoolOptions`를 사용한다. 기존 public API는 완전히 유지.

**Files:**
- Modify: `src/LMSupply.Generator/GeneratorPool.cs`
- Create: `src/LMSupply.Generator/Internal/GeneratorLoader.cs`

**Step 1: 기존 GeneratorPool 테스트 확인 (회귀 방지)**

```bash
dotnet test tests/LMSupply.Generator.Tests -v
```

테스트 수를 기록해둔다. 리팩터링 후 같은 수가 통과해야 함.

**Step 2: `GeneratorLoader.cs` 작성 (internal 로더)**

파일: `src/LMSupply.Generator/Internal/GeneratorLoader.cs`

```csharp
using LMSupply.Generator.Abstractions;
using LMSupply.Pool;

namespace LMSupply.Generator.Internal;

internal sealed class GeneratorLoader : IModelLoader<IGeneratorModel, GeneratorOptions>
{
    private readonly IGeneratorModelFactory _factory;

    public GeneratorLoader(IGeneratorModelFactory factory)
    {
        _factory = factory;
    }

    public Task<IGeneratorModel> LoadAsync(string modelId, GeneratorOptions? options, CancellationToken ct)
        => _factory.LoadAsync(modelId, options, ct);

    public long EstimateMemoryBytes(string modelId, GeneratorOptions? options)
    {
        GeneratorModelRegistry.Default.TryResolve(modelId, out var modelInfo);
        if (modelInfo != null)
        {
            var config = modelInfo.GetMemoryConfig(options?.MaxContextLength);
            return MemoryEstimator.Calculate(config).TotalBytes;
        }

        return MemoryEstimator.Calculate(new ModelMemoryConfig
        {
            ParameterCount = 3_000_000_000,
            NumLayers = 32,
            HiddenSize = 2560,
            ContextLength = options?.MaxContextLength ?? 4096,
            Quantization = Quantization.Quant4
        }).TotalBytes;
    }
}
```

**Step 3: `GeneratorPool.cs` 리팩터링**

`GeneratorPool.cs` 전체를 아래로 교체 (기존 `GeneratorPoolOptions`와 `LoadedModelInfo` 타입 별칭 추가로 하위 호환 유지):

```csharp
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Internal;
using LMSupply.Pool;

namespace LMSupply.Generator;

/// <summary>
/// Pool for managing multiple generator model instances with memory protection.
/// Delegates to the generic ModelPool infrastructure.
/// </summary>
public sealed class GeneratorPool : IAsyncDisposable
{
    private readonly ModelPool<IGeneratorModel, GeneratorOptions> _inner;

    /// <summary>
    /// Creates a new generator pool with the specified options.
    /// </summary>
    public GeneratorPool(IGeneratorModelFactory factory, GeneratorPoolOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var poolOptions = options != null
            ? new ModelPoolOptions
            {
                MaxMemoryBytes = options.MaxMemoryBytes,
                MemorySafetyMargin = options.MemorySafetyMargin,
                MaxLoadedModels = options.MaxLoadedModels,
            }
            : null;
        _inner = new ModelPool<IGeneratorModel, GeneratorOptions>(new GeneratorLoader(factory), poolOptions);
    }

    /// <summary>Gets the number of loaded models.</summary>
    public int LoadedModelCount => _inner.LoadedModelCount;

    /// <summary>Gets the total allocated memory across all loaded models.</summary>
    public long AllocatedMemoryBytes => _inner.AllocatedMemoryBytes;

    /// <summary>Gets the available memory for model loading.</summary>
    public long AvailableMemoryBytes => _inner.AvailableMemoryBytes;

    /// <summary>Gets or loads a generator model by ID.</summary>
    public Task<IGeneratorModel> GetOrLoadAsync(
        string modelId,
        GeneratorOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.GetOrLoadAsync(modelId, options, cancellationToken);

    /// <summary>Checks if a model is currently loaded.</summary>
    public bool IsLoaded(string modelId) => _inner.IsLoaded(modelId);

    /// <summary>Unloads a specific model.</summary>
    public Task UnloadAsync(string modelId, CancellationToken cancellationToken = default)
        => _inner.UnloadAsync(modelId, cancellationToken);

    /// <summary>Unloads all models.</summary>
    public Task UnloadAllAsync(CancellationToken cancellationToken = default)
        => _inner.UnloadAllAsync(cancellationToken);

    /// <summary>Gets information about all loaded models.</summary>
    public IReadOnlyList<LoadedModelInfo> GetLoadedModels() => _inner.GetLoadedModels();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

// Backward-compatible type alias kept in Generator namespace
/// <summary>Configuration options for the generator pool.</summary>
public sealed class GeneratorPoolOptions
{
    /// <summary>Maximum memory to allocate for models (in bytes).</summary>
    public long? MaxMemoryBytes { get; set; }
    /// <summary>Safety margin for memory calculations (0.0-1.0). Defaults to 0.2.</summary>
    public double MemorySafetyMargin { get; set; } = 0.2;
    /// <summary>Maximum number of models to keep loaded simultaneously. Defaults to 2.</summary>
    public int MaxLoadedModels { get; set; } = 2;
}
```

**주의:** `LoadedModelInfo`는 Core로 이동했으므로 Generator 패키지 `GlobalUsings.cs` 또는 개별 파일에 `using LMSupply.Pool;`을 추가해야 한다.

**Step 4: 빌드 오류 수정**

```bash
dotnet build src/LMSupply.Generator
```

오류가 있으면 수정. 주로 `LoadedModelInfo` 참조가 Core 네임스페이스(`LMSupply.Pool`)로 변경되어야 할 부분 확인.

**Step 5: GeneratorPool 회귀 테스트 통과 확인**

```bash
dotnet test tests/LMSupply.Generator.Tests -v
```

예상: Task 4 Step 1에서 기록한 테스트 수와 동일하게 PASS

**Step 6: 커밋**

```bash
git add src/LMSupply.Generator/
git commit -m "refactor(generator): delegate GeneratorPool to generic ModelPool"
```

---

### Task 5: `LocalGenerator.Pool` 프로퍼티 추가

**Files:**
- Modify: `src/LMSupply.Generator/LocalGenerator.cs`
- Create: `src/LMSupply.Generator/Internal/DefaultGeneratorFactory.cs`

**Step 1: 실패 테스트 작성**

파일: `tests/LMSupply.Generator.Tests/LocalGeneratorPoolTests.cs`

```csharp
using FluentAssertions;
using LMSupply.Generator;

namespace LMSupply.Generator.Tests;

public class LocalGeneratorPoolTests
{
    [Fact]
    public void LocalGenerator_HasPoolProperty()
    {
        var pool = LocalGenerator.Pool;
        pool.Should().NotBeNull();
    }

    [Fact]
    public void LocalGenerator_Pool_IsSingletonInstance()
    {
        var p1 = LocalGenerator.Pool;
        var p2 = LocalGenerator.Pool;
        p1.Should().BeSameAs(p2);
    }
}
```

**Step 2: 테스트 실패 확인**

```bash
dotnet test tests/LMSupply.Generator.Tests --filter "LocalGeneratorPoolTests" -v
```

예상: FAIL — `LocalGenerator.Pool` 없음

**Step 3: `DefaultGeneratorFactory.cs` 작성**

```csharp
using LMSupply.Generator.Abstractions;

namespace LMSupply.Generator.Internal;

/// <summary>
/// Default factory that uses LocalGenerator.LoadAsync for the pool.
/// </summary>
internal sealed class DefaultGeneratorFactory : IGeneratorModelFactory
{
    public static readonly DefaultGeneratorFactory Instance = new();

    public Task<IGeneratorModel> LoadAsync(
        string modelId,
        GeneratorOptions? options,
        CancellationToken cancellationToken)
        => LocalGenerator.LoadAsync(modelId, options, null, cancellationToken);
}
```

파일: `src/LMSupply.Generator/Internal/DefaultGeneratorFactory.cs`

**Step 4: `LocalGenerator.cs`에 Pool 프로퍼티 추가**

`LocalGenerator.cs`에 다음 프로퍼티를 추가:

```csharp
/// <summary>
/// Shared pool for named model management. Supports GetOrLoadAsync / UnloadAsync by model ID.
/// </summary>
public static GeneratorPool Pool { get; } = new(Internal.DefaultGeneratorFactory.Instance);
```

**Step 5: 테스트 통과 확인**

```bash
dotnet test tests/LMSupply.Generator.Tests --filter "LocalGeneratorPoolTests" -v
```

예상: PASS

**Step 6: 커밋**

```bash
git add src/LMSupply.Generator/ tests/LMSupply.Generator.Tests/
git commit -m "feat(generator): add LocalGenerator.Pool singleton property"
```

---

### Task 6: Embedder, Reranker 도메인에 Pool 추가

**Files (Embedder):**
- Create: `src/LMSupply.Embedder/Pool/EmbedderLoader.cs`
- Modify: `src/LMSupply.Embedder/LocalEmbedder.cs`

**Files (Reranker):**
- Create: `src/LMSupply.Reranker/Pool/RerankerLoader.cs`
- Modify: `src/LMSupply.Reranker/LocalReranker.cs`

**Step 1: 실패 테스트 작성**

파일: `tests/LMSupply.Embedder.Tests/LocalEmbedderPoolTests.cs`

```csharp
using FluentAssertions;
using LMSupply.Embedder;

namespace LMSupply.Embedder.Tests;

public class LocalEmbedderPoolTests
{
    [Fact]
    public void LocalEmbedder_HasPoolProperty()
    {
        LocalEmbedder.Pool.Should().NotBeNull();
    }

    [Fact]
    public void LocalEmbedder_Pool_IsSingleton()
    {
        LocalEmbedder.Pool.Should().BeSameAs(LocalEmbedder.Pool);
    }
}
```

파일: `tests/LMSupply.Reranker.Tests/LocalRerankerPoolTests.cs`

```csharp
using FluentAssertions;
using LMSupply.Reranker;

namespace LMSupply.Reranker.Tests;

public class LocalRerankerPoolTests
{
    [Fact]
    public void LocalReranker_HasPoolProperty()
    {
        LocalReranker.Pool.Should().NotBeNull();
    }
}
```

**Step 2: 테스트 실패 확인**

```bash
dotnet test tests/LMSupply.Embedder.Tests --filter "LocalEmbedderPoolTests" -v
dotnet test tests/LMSupply.Reranker.Tests --filter "LocalRerankerPoolTests" -v
```

예상: FAIL — `Pool` 프로퍼티 없음

**Step 3: `EmbedderLoader.cs` 작성**

```csharp
using LMSupply.Pool;

namespace LMSupply.Embedder.Pool;

internal sealed class EmbedderLoader : IModelLoader<IEmbeddingModel, EmbedderOptions>
{
    public Task<IEmbeddingModel> LoadAsync(string modelId, EmbedderOptions? options, CancellationToken ct)
        => LocalEmbedder.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, EmbedderOptions? options)
    {
        EmbedderModelRegistry.Default.TryResolve(modelId, out var info);
        return info?.SizeBytes ?? 500_000_000; // 500 MB default
    }
}
```

파일: `src/LMSupply.Embedder/Pool/EmbedderLoader.cs`

**주의:** `EmbedderModelRegistry`의 `ModelInfo`에 `SizeBytes`가 없으면 `500_000_000` 상수를 직접 사용한다. 빌드 오류 시 `info?.SizeBytes ?? 500_000_000` → `500_000_000`으로 대체.

**Step 4: `LocalEmbedder.cs`에 Pool 추가**

`LocalEmbedder.cs`의 클래스 본문에 추가:

```csharp
/// <summary>
/// Shared pool for named model management.
/// </summary>
public static ModelPool<IEmbeddingModel, EmbedderOptions> Pool { get; }
    = new(new Pool.EmbedderLoader());
```

필요한 using 추가: `using LMSupply.Pool;`

**Step 5: `RerankerLoader.cs` 작성 (Reranker 도메인 동일 패턴)**

`SizeBytes`가 `ModelInfo.cs`에 있으므로 활용:

```csharp
using LMSupply.Pool;

namespace LMSupply.Reranker.Pool;

internal sealed class RerankerLoader : IModelLoader<IRerankModel, RerankerOptions>
{
    public Task<IRerankModel> LoadAsync(string modelId, RerankerOptions? options, CancellationToken ct)
        => LocalReranker.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, RerankerOptions? options)
    {
        RerankerModelRegistry.Default.TryResolve(modelId, out var info);
        return info?.SizeBytes ?? 500_000_000;
    }
}
```

파일: `src/LMSupply.Reranker/Pool/RerankerLoader.cs`

**Step 6: `LocalReranker.cs`에 Pool 추가**

```csharp
public static ModelPool<IRerankModel, RerankerOptions> Pool { get; }
    = new(new Pool.RerankerLoader());
```

**Step 7: 빌드 오류 수정 후 테스트 통과 확인**

```bash
dotnet build
dotnet test tests/LMSupply.Embedder.Tests --filter "LocalEmbedderPoolTests" -v
dotnet test tests/LMSupply.Reranker.Tests --filter "LocalRerankerPoolTests" -v
```

**Step 8: 커밋**

```bash
git add src/LMSupply.Embedder/ src/LMSupply.Reranker/ tests/LMSupply.Embedder.Tests/ tests/LMSupply.Reranker.Tests/
git commit -m "feat(embedder,reranker): add Pool property to LocalEmbedder and LocalReranker"
```

---

### Task 7: Transcriber, Translator, Synthesizer 도메인에 Pool 추가

동일 패턴 반복. 아래는 각 도메인의 Loader 핵심 코드만 기재.

**Files:**
- Create: `src/LMSupply.Transcriber/Pool/TranscriberLoader.cs`
- Modify: `src/LMSupply.Transcriber/LocalTranscriber.cs`
- Create: `src/LMSupply.Translator/Pool/TranslatorLoader.cs`
- Modify: `src/LMSupply.Translator/LocalTranslator.cs`
- Create: `src/LMSupply.Synthesizer/Pool/SynthesizerLoader.cs`
- Modify: `src/LMSupply.Synthesizer/LocalSynthesizer.cs`

**EstimateMemoryBytes 기본값:**

| 도메인 | 기본값 |
|--------|--------|
| Transcriber | 1_000_000_000 (1 GB) |
| Translator | 500_000_000 (500 MB) |
| Synthesizer | 200_000_000 (200 MB) |

**각 Loader 패턴 (TranscriberLoader 예시):**

```csharp
using LMSupply.Pool;

namespace LMSupply.Transcriber.Pool;

internal sealed class TranscriberLoader : IModelLoader<ITranscriberModel, TranscriberOptions>
{
    public Task<ITranscriberModel> LoadAsync(string modelId, TranscriberOptions? options, CancellationToken ct)
        => LocalTranscriber.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, TranscriberOptions? options)
    {
        TranscriberModelRegistry.Default.TryResolve(modelId, out var info);
        return info?.SizeBytes ?? 1_000_000_000;
    }
}
```

**각 Local{Domain}.cs에 추가:**

```csharp
public static ModelPool<ITranscriberModel, TranscriberOptions> Pool { get; }
    = new(new Pool.TranscriberLoader());
```

**Step 1: 실패 테스트 작성 (각 도메인별)**

```csharp
// tests/LMSupply.Transcriber.Tests/LocalTranscriberPoolTests.cs
[Fact] public void LocalTranscriber_HasPoolProperty() => LocalTranscriber.Pool.Should().NotBeNull();

// tests/LMSupply.Translator.Tests/LocalTranslatorPoolTests.cs
[Fact] public void LocalTranslator_HasPoolProperty() => LocalTranslator.Pool.Should().NotBeNull();

// tests/LMSupply.Synthesizer.Tests/LocalSynthesizerPoolTests.cs
[Fact] public void LocalSynthesizer_HasPoolProperty() => LocalSynthesizer.Pool.Should().NotBeNull();
```

**Step 2: 구현 후 전체 빌드/테스트**

```bash
dotnet build
dotnet test tests/LMSupply.Transcriber.Tests tests/LMSupply.Translator.Tests tests/LMSupply.Synthesizer.Tests --filter "*PoolTests*" -v
```

**Step 3: 커밋**

```bash
git add src/LMSupply.Transcriber/ src/LMSupply.Translator/ src/LMSupply.Synthesizer/
git add tests/LMSupply.Transcriber.Tests/ tests/LMSupply.Translator.Tests/ tests/LMSupply.Synthesizer.Tests/
git commit -m "feat(transcriber,translator,synthesizer): add Pool property"
```

---

### Task 8: Captioner, Detector, Segmenter, Ocr, ImageGenerator 도메인에 Pool 추가

**EstimateMemoryBytes 기본값:**

| 도메인 | 기본값 |
|--------|--------|
| Captioner | 500_000_000 (500 MB) |
| Detector | 100_000_000 (100 MB) |
| Segmenter | 200_000_000 (200 MB) |
| Ocr | 100_000_000 (100 MB) |
| ImageGenerator | 4_000_000_000 (4 GB) |

**각 도메인에 동일 패턴 적용:**
1. `src/LMSupply.{Domain}/Pool/{Domain}Loader.cs` 생성
2. `Local{Domain}.cs`에 `Pool` 프로퍼티 추가
3. 간단한 singleton 테스트 작성

**Step 1: 실패 테스트 작성**

```csharp
// 각 도메인 테스트 파일에 추가
[Fact] public void Local{Domain}_HasPoolProperty() => Local{Domain}.Pool.Should().NotBeNull();
```

대상: Captioner, Detector, Segmenter, Ocr, ImageGenerator (5개)

**Step 2: 구현 후 빌드/테스트**

```bash
dotnet build
dotnet test tests/LMSupply.Captioner.Tests tests/LMSupply.Detector.Tests tests/LMSupply.Segmenter.Tests tests/LMSupply.Ocr.Tests tests/LMSupply.ImageGenerator.Tests --filter "*PoolTests*" -v
```

**Step 3: 커밋**

```bash
git add src/LMSupply.Captioner/ src/LMSupply.Detector/ src/LMSupply.Segmenter/ src/LMSupply.Ocr/ src/LMSupply.ImageGenerator/
git add tests/LMSupply.Captioner.Tests/ tests/LMSupply.Detector.Tests/ tests/LMSupply.Segmenter.Tests/ tests/LMSupply.Ocr.Tests/ tests/LMSupply.ImageGenerator.Tests/
git commit -m "feat(vision,ocr): add Pool property to remaining domain entry points"
```

---

### Task 9: 최종 검증

**Step 1: 전체 빌드 확인**

```bash
dotnet build
```

예상: 0 errors, 0 warnings (경고는 확인 후 적절히 처리)

**Step 2: 전체 단위 테스트 실행 (Integration 제외)**

```bash
dotnet test --filter "Category!=Integration"
```

예상: 모든 테스트 PASS

**Step 3: 회귀 확인: Whisper quantization 테스트**

```bash
dotnet test tests/LMSupply.Core.Tests --filter "WhisperQuantizationSelectionTests" -v
```

예상: PASS (이전 버그 수정분 유지)

**Step 4: 버전 범프 및 최종 커밋**

`Directory.Build.props` 또는 각 `.csproj`의 버전을 패치 버전 bump:

```bash
# 현재 버전 확인
grep -r "Version>" src/LMSupply.Core/LMSupply.Core.csproj
```

`0.16.1` → `0.16.2` (패치: 기능 추가지만 Core API 확장은 마이너로 고려)

실제로는 새 Pool API 추가이므로 `0.17.0`으로 bump:

```bash
git add .
git commit -m "feat: add generic ModelPool to all 10 domains and refactor GeneratorPool (0.17.0)"
```

---

## 완료 기준 체크리스트

- [ ] `src/LMSupply.Core/Pool/` — `IModelLoader.cs`, `ModelPool.cs`, `ModelPoolOptions.cs`, `LoadedModelInfo.cs` 4개 파일 존재
- [ ] 11개 도메인 (`Generator` + 10개) 모두 `Pool` 프로퍼티 노출
- [ ] `GeneratorPool` 기존 API 완전 호환 (breaking change 없음)
- [ ] `dotnet test --filter "Category!=Integration"` 모두 PASS
- [ ] `LocalEmbedder.Pool.GetOrLoadAsync("default")` 컴파일 가능 (코드 작성 불필요, 타입 체크만)
