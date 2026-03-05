# ModelPool 설계: 공통 풀 추상화

**날짜:** 2026-03-05
**상태:** 승인됨

## 배경

lm-supply는 모든 도메인에 `LoadAsync` / `WarmupAsync`를 제공하지만,
이름으로 named load/unload/조회를 할 수 있는 Pool 구조는 `GeneratorPool`(Generator 도메인)에만 존재한다.
나머지 9개 도메인(Embedder, Reranker, Transcriber, Translator, Synthesizer,
Captioner, Detector, Segmenter, Ocr, ImageGenerator)은 `DisposeAsync()`를 통한
암묵적 unload만 가능하다.

## 목표

- 모든 도메인에서 일관된 `Pool.GetOrLoadAsync` / `Pool.UnloadAsync` API 제공
- 제네릭 `ModelPool<TModel, TOptions>` 추상화 한 곳에서 관리 (LRU + 메모리 기반)
- `GeneratorPool`을 새 추상화 위에서 동작하도록 리팩터링 (기존 API 호환 유지)
- HTTP API 서버 / UI 모델 관리 등 Out-of-Scope 항목은 포함하지 않음

## 설계

### 1. 핵심 추상화 (`LMSupply.Core`)

```csharp
// 각 도메인이 구현하는 로더 인터페이스
public interface IModelLoader<TModel, TOptions>
    where TModel : IAsyncDisposable
    where TOptions : class
{
    Task<TModel> LoadAsync(string modelId, TOptions? options, CancellationToken ct);
    long EstimateMemoryBytes(string modelId, TOptions? options);
}

// 제네릭 풀 — LRU eviction + 메모리 안전 마진
public sealed class ModelPool<TModel, TOptions> : IAsyncDisposable
    where TModel : IAsyncDisposable
    where TOptions : class
{
    public Task<TModel> GetOrLoadAsync(string modelId, TOptions? options = null, CancellationToken ct = default);
    public Task UnloadAsync(string modelId, CancellationToken ct = default);
    public Task UnloadAllAsync(CancellationToken ct = default);
    public bool IsLoaded(string modelId);
    public IReadOnlyList<LoadedModelInfo> GetLoadedModels();
    public int LoadedModelCount { get; }
    public long AllocatedMemoryBytes { get; }
    public long AvailableMemoryBytes { get; }
}
```

**Core로 이동:**
- `ModelPoolOptions` — Generator 패키지에서 이동
- `LoadedModelInfo` — Generator 패키지에서 이동

### 2. 도메인 통합

각 도메인 패키지에:

- **internal** `{Domain}Loader : IModelLoader<I{Domain}Model, {Domain}Options>` 추가
- `Local{Domain}.Pool` 정적 싱글턴 프로퍼티 노출

```csharp
public static class LocalEmbedder
{
    // 기존 API 유지
    public static Task<IEmbeddingModel> LoadAsync(string modelId, ...) { ... }

    // 신규
    public static ModelPool<IEmbeddingModel, EmbedderOptions> Pool { get; }
        = new(new EmbedderLoader());
}

internal sealed class EmbedderLoader : IModelLoader<IEmbeddingModel, EmbedderOptions>
{
    public Task<IEmbeddingModel> LoadAsync(string modelId, EmbedderOptions? options, CancellationToken ct)
        => LocalEmbedder.LoadAsync(modelId, options, null, ct);

    public long EstimateMemoryBytes(string modelId, EmbedderOptions? options)
    {
        EmbedderModelRegistry.Default.TryResolve(modelId, out var info);
        return info?.SizeBytes ?? 500_000_000;
    }
}
```

**`GeneratorPool` 리팩터링:**

```csharp
// 기존 GeneratorPool API 완전 유지 (breaking change 없음)
public sealed class GeneratorPool : IAsyncDisposable
{
    private readonly ModelPool<IGeneratorModel, GeneratorOptions> _inner;

    public Task<IGeneratorModel> GetOrLoadAsync(...) => _inner.GetOrLoadAsync(...);
    public Task UnloadAsync(string modelId, ...) => _inner.UnloadAsync(modelId, ...);
    public Task UnloadAllAsync(...) => _inner.UnloadAllAsync(...);
    public bool IsLoaded(string modelId) => _inner.IsLoaded(modelId);
    public IReadOnlyList<LoadedModelInfo> GetLoadedModels() => _inner.GetLoadedModels();
    // ...
}
```

`LocalGenerator.Pool` 프로퍼티도 추가 (기존 `GeneratorPool` 인스턴스 반환).

### 3. 파일 구조

```
src/LMSupply.Core/
  Pool/
    IModelLoader.cs          (신규)
    ModelPool.cs             (신규)
    ModelPoolOptions.cs      (Generator → Core 이동)
    LoadedModelInfo.cs       (Generator → Core 이동)

src/LMSupply.{Domain}/       (10개 도메인 각각)
  Pool/
    {Domain}Loader.cs        (신규, internal)
  Local{Domain}.cs           (Pool 프로퍼티 추가)

src/LMSupply.Generator/
  GeneratorPool.cs           (ModelPool<> 위임 구조로 교체)
  GeneratorPoolOptions.cs    (삭제 → Core의 ModelPoolOptions 사용)
```

### 4. EstimateMemoryBytes 기본값

| 도메인 | 기본값 |
|--------|--------|
| Embedder | 500 MB |
| Reranker | 500 MB |
| Transcriber | 1 GB |
| Translator | 500 MB |
| Synthesizer | 200 MB |
| Captioner | 500 MB |
| Detector | 100 MB |
| Segmenter | 200 MB |
| Ocr | 100 MB |
| ImageGenerator | 4 GB |

### 5. 최종 사용 예시

```csharp
// 이름으로 load
var model = await LocalEmbedder.Pool.GetOrLoadAsync("default");

// 이름으로 unload
await LocalEmbedder.Pool.UnloadAsync("default");

// 로드된 모델 목록
var loaded = LocalTranscriber.Pool.GetLoadedModels();

// GeneratorPool 기존 코드 그대로 동작
var generator = await LocalGenerator.Pool.GetOrLoadAsync("default");
```

## Out-of-Scope

- HTTP/REST API 서버
- Download 이벤트 시스템
- UI 모델 관리 기능
