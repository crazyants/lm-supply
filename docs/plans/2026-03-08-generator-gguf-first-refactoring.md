# Generator GGUF-First 리팩토링 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Generator 도메인을 GGUF 기본 백엔드로 전환하고, tool calling 지원 모델로 레지스트리를 교체한다.

**Architecture:** llama-server에 `--jinja` 플래그 추가로 GGUF tool calling 즉시 활성화. GgufModelRegistry를 llama.cpp 네이티브 핸들러 지원 모델(Option A: 안정성)로 전면 교체. auto 분기를 "DirectML/NPU면 ONNX, 그 외 GGUF"로 변경. v0.x이므로 하위호환 없이 과감히 교체.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, llama.cpp/llama-server, onnxruntime-genai

---

## Task 1: llama-server `--jinja` 플래그 추가

**Files:**
- Modify: `src/LMSupply.Llama/Server/LlamaServerProcess.cs:404-416`

**Step 1: `BuildArguments()`에 `--jinja` 추가**

`BuildArguments()` 메서드의 기본 인자 리스트에 `"--jinja"` 추가. `--cont-batching` 바로 뒤에 배치.

```csharp
// line 415-416 변경
"--cont-batching",     // Enable continuous batching for better throughput
"--jinja"              // Enable Jinja template for native tool calling support
```

**Step 2: 빌드 확인**

Run: `dotnet build src/LMSupply.Llama/`
Expected: Build succeeded

**Step 3: 커밋**

```
feat: add --jinja flag to llama-server for native tool calling
```

---

## Task 2: GgufModelRegistry 모델 전면 교체

**Files:**
- Modify: `src/LMSupply.Generator/Internal/Llama/GgufModelRegistry.cs`
- Modify: `tests/LMSupply.Generator.Tests/GgufModelRegistryTests.cs`

**Step 1: GgufModelRegistry 모델 교체**

기존 8개 모델(default, fast, quality, large, multilingual, korean, code, reasoning) 전부 삭제하고, tool-calling 안정성 기준 5개 모델로 교체:

```csharp
private static readonly Dictionary<string, GgufModelInfo> _models = new(StringComparer.OrdinalIgnoreCase)
{
    // ============================================================
    // Tool-Calling First: llama.cpp 네이티브 핸들러 안정성 기준
    // Option A (안정성 최우선) — Hermes/Mistral/Llama 핸들러
    // ============================================================

    // Fast: Smallest tool-calling capable model (Mistral Nemo handler)
    ["gguf:fast"] = new GgufModelInfo
    {
        RepoId = "mistralai/Ministral-3-3B-Instruct-2512-GGUF",
        DisplayName = "Ministral 3 3B Instruct",
        DefaultFile = "Ministral-3-3B-Instruct-2512-Q4_K_M.gguf",
        ChatFormat = "mistral-nemo",
        ContextLength = 32768,
        ParameterCount = 3_000_000_000,
        License = LicenseTier.MIT,
        LicenseName = "Apache 2.0",
    },

    // Default: Most stable tool-calling model (Hermes native handler)
    ["gguf:default"] = new GgufModelInfo
    {
        RepoId = "NousResearch/Hermes-3-Llama-3.1-8B-GGUF",
        DisplayName = "Hermes 3 Llama 3.1 8B",
        DefaultFile = "Hermes-3-Llama-3.1-8B.Q4_K_M.gguf",
        ChatFormat = "chatml",
        ContextLength = 8192,
        ParameterCount = 8_000_000_000,
        License = LicenseTier.Conditional,
        LicenseName = "Llama 3.1 Community License",
        LicenseRestrictions = "700M MAU limit for commercial use"
    },

    // Quality: Mistral Nemo native handler, Apache 2.0
    ["gguf:quality"] = new GgufModelInfo
    {
        RepoId = "bartowski/Mistral-Nemo-Instruct-2407-GGUF",
        DisplayName = "Mistral Nemo 12B Instruct",
        DefaultFile = "Mistral-Nemo-Instruct-2407-Q4_K_M.gguf",
        ChatFormat = "mistral-nemo",
        ContextLength = 32768,
        ParameterCount = 12_000_000_000,
        License = LicenseTier.MIT,
        LicenseName = "Apache 2.0",
    },

    // Large: High quality dense model
    ["gguf:large"] = new GgufModelInfo
    {
        RepoId = "unsloth/Qwen3-32B-GGUF",
        DisplayName = "Qwen 3 32B",
        DefaultFile = "Qwen3-32B-Q4_K_M.gguf",
        ChatFormat = "chatml",
        ContextLength = 32768,
        ParameterCount = 32_000_000_000,
        License = LicenseTier.MIT,
        LicenseName = "Apache 2.0",
    },

    // XLarge: Server-grade MoE model
    ["gguf:xlarge"] = new GgufModelInfo
    {
        RepoId = "unsloth/Qwen3.5-122B-A10B-GGUF",
        DisplayName = "Qwen 3.5 122B A10B (MoE)",
        DefaultFile = "Qwen3.5-122B-A10B-Q4_K_M.gguf",
        ChatFormat = "chatml",
        ContextLength = 32768,
        ParameterCount = 122_000_000_000,
        License = LicenseTier.MIT,
        LicenseName = "Apache 2.0",
    },
};
```

`GetAutoModel()` 교체:

```csharp
public static GgufModelInfo GetAutoModel()
{
    var tier = HardwareProfile.Current.Tier;

    return tier switch
    {
        PerformanceTier.Ultra => _models["gguf:large"],    // Qwen3 32B
        PerformanceTier.High => _models["gguf:quality"],   // Mistral Nemo 12B
        PerformanceTier.Medium => _models["gguf:default"], // Hermes 3 8B
        _ => _models["gguf:fast"]                          // Ministral 3 3B
    };
}
```

**Step 2: 테스트 업데이트**

`GgufModelRegistryTests.cs` 전면 재작성. 삭제된 alias(multilingual, korean, code, reasoning)를 제거하고, 새 alias(xlarge)를 추가. `DefaultModel_HasValidConfiguration`에서 Hermes 검증. `AllModels_HaveValidChatFormats`에서 새 포맷 반영.

```csharp
[Theory]
[InlineData("gguf:default")]
[InlineData("gguf:fast")]
[InlineData("gguf:quality")]
[InlineData("gguf:large")]
[InlineData("gguf:xlarge")]
public void Resolve_WithPrefixedAlias_ReturnsModelInfo(string alias)
{
    var result = GgufModelRegistry.Resolve(alias);
    result.Should().NotBeNull();
    result!.RepoId.Should().NotBeNullOrWhiteSpace();
    result.DefaultFile.Should().EndWith(".gguf");
    result.ChatFormat.Should().NotBeNullOrWhiteSpace();
}

[Theory]
[InlineData("default")]
[InlineData("fast")]
[InlineData("quality")]
public void Resolve_WithoutPrefix_ReturnsModelInfo(string alias)
{
    var result = GgufModelRegistry.Resolve(alias);
    result.Should().NotBeNull();
    result!.RepoId.Should().Contain("/");
}

[Fact]
public void DefaultModel_HasValidConfiguration()
{
    var model = GgufModelRegistry.Resolve("gguf:default");
    model.Should().NotBeNull();
    model!.RepoId.Should().Contain("Hermes");
    model.ChatFormat.Should().Be("chatml");
    model.DefaultFile.Should().Contain("Q4_K_M");
    model.ContextLength.Should().BeGreaterThanOrEqualTo(4096);
}

[Fact]
public void AllModels_HaveValidChatFormats()
{
    var validFormats = new[] { "chatml", "mistral-nemo" };
    var models = GgufModelRegistry.GetAllModels();
    models.Should().AllSatisfy(m =>
    {
        validFormats.Should().Contain(m.ChatFormat,
            $"Model {m.DisplayName} has unexpected chat format: {m.ChatFormat}");
    });
}

[Fact]
public void GetAliases_ReturnsExpectedAliases()
{
    var aliases = GgufModelRegistry.GetAliases();
    aliases.Should().Contain("gguf:default");
    aliases.Should().Contain("gguf:fast");
    aliases.Should().Contain("gguf:quality");
    aliases.Should().Contain("gguf:large");
    aliases.Should().Contain("gguf:xlarge");
}
```

**Step 3: 테스트 실행**

Run: `dotnet test tests/LMSupply.Generator.Tests --filter "FullyQualifiedName~GgufModelRegistry" -v normal`
Expected: All tests pass

**Step 4: 커밋**

```
feat: replace GgufModelRegistry with tool-calling models (Option A stability)
```

---

## Task 3: DefaultGeneratorModels ONNX 정리

**Files:**
- Modify: `src/LMSupply.Generator/Models/DefaultGeneratorModels.cs`
- Modify: `tests/LMSupply.Generator.Tests/ModelRegistryTests.cs`

**Step 1: ONNX 모델 정리**

FC 미지원 모델 제거 (Llama321B, Llama323B, Gemma22B). `fast` alias를 Phi4Mini로 재배치.

```csharp
public static class DefaultGeneratorModels
{
    public static ModelInfo Default => Phi4Mini;

    // ===== ONNX models with tool calling support =====

    // Phi4Mini: 유지 (default + fast)
    public static ModelInfo Phi4Mini { get; } = new()
    {
        ModelId = "microsoft/Phi-4-mini-instruct-onnx",
        AliasName = "default",
        DisplayName = "Phi-4 Mini",
        Description = "Default: Phi-4 Mini, 3.8B params, MIT, 16K context, tool calling",
        ParameterCount = 3_800_000_000,
        License = LicenseTier.MIT,
        LicenseName = "MIT",
        ChatFormat = "phi3",
        DefaultQuantization = Quantization.Quant4,
        RecommendedContextLength = 16384,
        NumLayers = 32,
        HiddenSize = 3072,
        Subfolder = "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4"
    };

    // Phi35Mini: 유지 (레거시 호환)
    public static ModelInfo Phi35Mini { get; } = new() { /* 기존과 동일 */ };

    // Phi4: 유지 (quality)
    public static ModelInfo Phi4 { get; } = new() { /* 기존과 동일 */ };

    // fast alias → Phi4Mini 동일 (1B FC 미지원이므로)
    public static ModelInfo Fast => Phi4Mini;

    public static IReadOnlyList<ModelInfo> All { get; } =
    [
        Phi4Mini,   // default
        Phi35Mini,  // phi-3.5-mini (legacy)
        Phi4,       // quality
    ];
}
```

**Step 2: 테스트 업데이트**

- `Resolve_WithFastAlias_ReturnsLlama1B` → `Resolve_WithFastAlias_ReturnsPhi4Mini`로 변경. `fast`가 없으면 `default`로 폴백.
- `DefaultGeneratorModels_All_ShouldContainAllModels` → `HaveCount(3)`으로 변경, Llama/Gemma 참조 제거.
- `AllModels_HaveValidChatFormat` → `"phi3"` only.
- Llama/Gemma 관련 `TryResolve_KnownModel_HasCorrectLicense` InlineData 제거.

**Step 3: 테스트 실행**

Run: `dotnet test tests/LMSupply.Generator.Tests --filter "FullyQualifiedName~ModelRegistryTests" -v normal`
Expected: All tests pass

**Step 4: 커밋**

```
feat: remove FC-incapable ONNX models, keep Phi-4 series only
```

---

## Task 4: GeneratorModelRegistry auto 로직 수정

**Files:**
- Modify: `src/LMSupply.Generator/Models/GeneratorModelRegistry.cs`

**Step 1: `GetAutoModel()` 단순화**

ONNX auto는 DirectML/NPU에서만 사용되므로 Phi-4 시리즈로 단순화:

```csharp
protected override ModelInfo GetAutoModel()
{
    var tier = HardwareProfile.Current.Tier;
    Trace.TraceInformation($"[GeneratorModelRegistry] Auto-selecting ONNX model for tier: {tier}");

    var model = tier switch
    {
        PerformanceTier.Ultra => DefaultGeneratorModels.Phi4,      // 14B
        PerformanceTier.High => DefaultGeneratorModels.Phi4,       // 14B
        _ => DefaultGeneratorModels.Phi4Mini                       // 3.8B
    };

    return model with { AliasName = "auto" };
}
```

**Step 2: 테스트 실행**

Run: `dotnet test tests/LMSupply.Generator.Tests --filter "FullyQualifiedName~ModelRegistryTests" -v normal`
Expected: Pass

**Step 3: 커밋**

```
refactor: simplify ONNX auto selection to Phi-4 series only
```

---

## Task 5: ModelFormatDetector 기본값 변경 + auto 분기

**Files:**
- Modify: `src/LMSupply.Generator/Internal/ModelFormatDetector.cs`
- Modify: `tests/LMSupply.Generator.Tests/ModelFormatDetectorTests.cs`

**Step 1: "auto" alias 처리 및 기본값 변경**

`Detect()` 메서드 변경:

1. "auto" 처리를 최상단에 추가 — 플랫폼 기반 분기
2. 기본값(맨 끝 fallback)을 ONNX→GGUF로 변경
3. Known GGUF provider에 `"unsloth"`, `"nousresearch"`, `"mistralai"` 추가 (GGUF 제공하는 공식 계정)

```csharp
public static ModelFormat Detect(string modelIdOrPath)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(modelIdOrPath);

    // 0. Handle "auto" — platform-based routing
    if (modelIdOrPath.Equals("auto", StringComparison.OrdinalIgnoreCase))
    {
        return ShouldPreferOnnx() ? ModelFormat.Onnx : ModelFormat.Gguf;
    }

    // 1. Check if it's a GGUF registry alias
    if (GgufModelRegistry.IsAlias(modelIdOrPath))
        return ModelFormat.Gguf;

    // 2. Check file extension
    if (HasExtension(modelIdOrPath, GgufExtensions))
        return ModelFormat.Gguf;
    if (HasExtension(modelIdOrPath, OnnxExtensions))
        return ModelFormat.Onnx;

    // 3. Check local directory
    if (Directory.Exists(modelIdOrPath))
        return DetectFromDirectory(modelIdOrPath);

    // 4. Check HuggingFace repo ID
    if (IsHuggingFaceRepoId(modelIdOrPath))
        return DetectFromRepoId(modelIdOrPath);

    // 5. Check local file path
    if (IsFilePath(modelIdOrPath))
    {
        var fileName = Path.GetFileName(modelIdOrPath);
        if (HasExtension(fileName, GgufExtensions))
            return ModelFormat.Gguf;
    }

    // 6. Check ONNX registry for known models
    if (GeneratorModelRegistry.Default.TryResolve(modelIdOrPath, out _))
        return ModelFormat.Onnx;

    // 7. Default: GGUF (GGUF-first strategy)
    return ModelFormat.Gguf;
}

/// <summary>
/// Determines if ONNX should be preferred based on hardware.
/// ONNX is only preferred for Windows DirectML (non-NVIDIA) or NPU environments.
/// </summary>
private static bool ShouldPreferOnnx()
{
    var profile = HardwareProfile.Current;

    // ONNX advantage: Windows DirectML for non-NVIDIA GPUs
    if (profile.RecommendedProvider == ExecutionProvider.DirectML &&
        profile.GpuInfo.Vendor != GpuVendor.Nvidia)
    {
        return true;
    }

    // All other cases: GGUF (CPU, CUDA, Metal, Linux)
    return false;
}
```

**Step 2: 테스트 업데이트**

```csharp
// 기존 "No format hint defaults to ONNX" → GGUF로 변경
[Theory]
[InlineData("microsoft/Phi-3.5-mini-instruct", ModelFormat.Gguf)]  // No format hint, defaults to GGUF
[InlineData("meta-llama/Llama-3.2-3B-Instruct", ModelFormat.Gguf)] // No format hint, defaults to GGUF
public void Detect_NoFormatHint_DefaultsToGguf(string repoId, ModelFormat expected)
{
    var result = ModelFormatDetector.Detect(repoId);
    result.Should().Be(expected);
}

// ONNX registry 모델은 여전히 ONNX
[Fact]
public void Detect_RegisteredOnnxModel_ReturnsOnnx()
{
    var result = ModelFormatDetector.Detect("default");
    result.Should().Be(ModelFormat.Onnx);
}

// "medium" alias는 레지스트리에 없으므로 GGUF로 폴백
[Theory]
[InlineData("fast", ModelFormat.Onnx)]     // ONNX 레지스트리에 있으면 ONNX
[InlineData("quality", ModelFormat.Onnx)]  // ONNX 레지스트리에 있으면 ONNX
[InlineData("medium", ModelFormat.Gguf)]   // 레지스트리에 없으면 GGUF
public void Detect_WellKnownAliases_CorrectFormat(string alias, ModelFormat expected)
{
    var result = ModelFormatDetector.Detect(alias);
    result.Should().Be(expected);
}
```

**Step 3: 테스트 실행**

Run: `dotnet test tests/LMSupply.Generator.Tests --filter "FullyQualifiedName~ModelFormatDetector" -v normal`
Expected: All pass

**Step 4: 커밋**

```
feat: change default format to GGUF, add auto platform-based routing
```

---

## Task 6: GeneratorModelLoader auto 분기 수정

**Files:**
- Modify: `src/LMSupply.Generator/Internal/GeneratorModelLoader.cs`

**Step 1: `LoadAsync()`에 auto 분기 추가**

"auto" modelId가 들어왔을 때 플랫폼 기반으로 GGUF/ONNX 분기. `LoadAsync()` 메서드 시작 부분에 auto 처리 추가:

```csharp
public static async Task<IGeneratorModel> LoadAsync(
    string modelId,
    GeneratorOptions options,
    IProgress<DownloadProgress>? progress,
    CancellationToken cancellationToken)
{
    // Detect model format from model ID
    var format = ModelFormatDetector.Detect(modelId);

    // Route to appropriate loader based on format
    return format switch
    {
        ModelFormat.Gguf => await LoadGgufAsync(modelId, options, progress, cancellationToken),
        ModelFormat.Onnx => await LoadOnnxAsync(modelId, options, progress, cancellationToken),
        ModelFormat.Unknown => await LoadGgufAsync(modelId, options, progress, cancellationToken), // GGUF fallback
        _ => throw new NotSupportedException($"Unsupported model format: {format}")
    };
}
```

핵심 변경: `ModelFormat.Unknown` fallback을 ONNX→GGUF로 변경.

**Step 2: 빌드 확인**

Run: `dotnet build src/LMSupply.Generator/`
Expected: Build succeeded

**Step 3: 커밋**

```
refactor: change Unknown format fallback from ONNX to GGUF
```

---

## Task 7: LocalGenerator auto 분기 수정

**Files:**
- Modify: `src/LMSupply.Generator/LocalGenerator.cs`

**Step 1: `LoadAsync()`에서 auto 처리 수정**

"auto" alias가 들어왔을 때 플랫폼에 따라 GGUF/ONNX 레지스트리를 분기하여 적절한 모델 선택:

```csharp
public static Task<IGeneratorModel> LoadAsync(
    string modelId,
    GeneratorOptions? options = null,
    IProgress<DownloadProgress>? progress = null,
    CancellationToken cancellationToken = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
    options ??= new GeneratorOptions();

    // Parse variant qualifier (e.g., "default:fp16" → modelId="default", hint="fp16")
    var (baseId, qualifier) = LMSupplyOptionsBase.SplitQualifier(modelId);
    modelId = baseId;
    options.QuantizationHint ??= qualifier;

    // Handle "auto" — platform-based format selection
    if (modelId.Equals("auto", StringComparison.OrdinalIgnoreCase))
    {
        return LoadAutoAsync(options, progress, cancellationToken);
    }

    // Handle standard aliases via the ONNX registry
    if (GeneratorModelRegistry.Default.TryResolve(modelId, out var resolvedModel))
    {
        modelId = resolvedModel!.ModelId;
    }

    // Check if it's a local file path
    if (File.Exists(modelId))
    {
        return Internal.GeneratorModelLoader.LoadFromPathAsync(modelId, options, modelId);
    }

    // Check if it's a local directory path
    if (Directory.Exists(modelId))
    {
        return Internal.GeneratorModelLoader.LoadFromPathAsync(modelId, options, modelId);
    }

    return Internal.GeneratorModelLoader.LoadAsync(modelId, options, progress, cancellationToken);
}

/// <summary>
/// Auto-selects the optimal model based on hardware platform.
/// GGUF for most environments (CPU, CUDA, Metal).
/// ONNX only for Windows DirectML (non-NVIDIA) or NPU.
/// </summary>
private static Task<IGeneratorModel> LoadAutoAsync(
    GeneratorOptions options,
    IProgress<DownloadProgress>? progress,
    CancellationToken cancellationToken)
{
    var profile = Hardware.HardwareProfile.Current;
    var useOnnx = profile.RecommendedProvider == Runtime.ExecutionProvider.DirectML &&
                  profile.GpuInfo.Vendor != Runtime.GpuVendor.Nvidia;

    if (useOnnx)
    {
        // ONNX path: DirectML/NPU advantage
        var model = GeneratorModelRegistry.Default.Resolve("auto");
        return Internal.GeneratorModelLoader.LoadAsync(
            model.ModelId, options, progress, cancellationToken);
    }
    else
    {
        // GGUF path: CPU, CUDA, Metal, Linux — all go here
        var model = Internal.Llama.GgufModelRegistry.GetAutoModel();
        return Internal.GeneratorModelLoader.LoadAsync(
            model.RepoId, options, progress, cancellationToken);
    }
}
```

**Step 2: `DefaultModel` 상수는 유지**

`DefaultModel` 상수는 `LoadDefaultAsync()`에서만 사용하므로 그대로 둔다. 사용자가 명시적으로 `LoadDefaultAsync()`를 호출하면 ONNX Phi-4-mini가 로드되는데, 이는 의도된 동작이다 (명시적 ONNX 선택).

**Step 3: 빌드 확인**

Run: `dotnet build src/LMSupply.Generator/`
Expected: Build succeeded

**Step 4: 커밋**

```
feat: implement platform-based auto selection (GGUF default, ONNX for DirectML)
```

---

## Task 8: 전체 빌드 및 테스트

**Step 1: 전체 빌드**

Run: `dotnet build`
Expected: Build succeeded

**Step 2: 전체 테스트 (integration 제외)**

Run: `dotnet test --filter "Category!=Integration" -v normal`
Expected: All tests pass

**Step 3: 실패 테스트 수정**

테스트 실패가 있으면 수정. 예상되는 실패:
- `ModelFormatDetectorTests` — 기본값 변경으로 인한 assertion 수정
- `ModelRegistryTests` — Llama/Gemma 모델 제거로 인한 참조 수정
- `GgufModelRegistryTests` — 삭제된 alias 참조

**Step 4: 최종 커밋**

```
fix: update remaining tests for GGUF-first refactoring
```

---

## Task 순서 요약

| Task | 파일 | 핵심 변경 | 위험도 |
|------|------|-----------|--------|
| 1 | LlamaServerProcess.cs | `--jinja` 추가 | 낮음 |
| 2 | GgufModelRegistry.cs + 테스트 | 모델 전면 교체 | 중간 |
| 3 | DefaultGeneratorModels.cs + 테스트 | FC 미지원 ONNX 제거 | 중간 |
| 4 | GeneratorModelRegistry.cs | auto 단순화 | 낮음 |
| 5 | ModelFormatDetector.cs + 테스트 | 기본값 GGUF + auto 분기 | 높음 |
| 6 | GeneratorModelLoader.cs | Unknown fallback GGUF | 낮음 |
| 7 | LocalGenerator.cs | auto 플랫폼 분기 | 높음 |
| 8 | - | 전체 빌드/테스트 | - |
