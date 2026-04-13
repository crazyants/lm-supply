# Generator Default Alias — Platform-aware Routing + ONNX Path Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `LocalGenerator.LoadAsync("default")` route through the `"auto"` hardware-aware selection so NVIDIA/CPU/macOS/Linux users get Gemma 4 GGUF automatically, while fixing the 2-level nested ONNX variant path resolution bug that blocks Phi-4 Mini ONNX.

**Architecture:**
Two orthogonal changes ship together as v0.28.0. The bug fix adds level-2 recursion to `OnnxGeneratorModelFactory.FindVariantSubfolder` plus diagnostic Trace logs; the alias redirect adds a `"default"` branch at the top of `LocalGenerator.LoadAsync` that delegates to the existing `LoadAutoAsync`. Phi-4 Mini stays accessible via the new explicit alias `"phi-4-mini"` and direct repo ID.

**Tech Stack:** .NET 10.0, xUnit 2.9.3, FluentAssertions 8.8.0, ONNX Runtime GenAI 0.12.2.

**Spec:** `docs/superpowers/specs/2026-04-13-generator-default-platform-aware-design.md`

---

## File Structure

### Files to modify
- `src/LMSupply.Generator/OnnxGeneratorModelFactory.cs` — add 2-level recursion to `FindVariantSubfolder`, add per-branch Trace logs in `ResolveModelPathWithBaseAsync`, improve `FileNotFoundException` message.
- `src/LMSupply.Generator/LocalGenerator.cs` — add `"default"` branch redirecting to `LoadAutoAsync`, remove `DefaultModel` const, update `LoadDefaultAsync` body, add Trace log in `LoadAutoAsync`.
- `src/LMSupply.Generator/Models/DefaultGeneratorModels.cs` — change `Phi4Mini.AliasName` from `"default"` to `"phi-4-mini"`.
- `Directory.Build.props` — bump `0.27.4` → `0.28.0`.
- `README.md` — restructure Generator Models section with a platform-based defaults matrix.
- `docs/generator.md` — mirror the README matrix and add explicit-model-selection examples.
- `ROADMAP.md` — add v0.28.0 entry.

### Files to create
- `tests/LMSupply.Generator.Tests/OnnxGeneratorPathResolutionTests.cs` — 2-level nested path, 1-level backward compat, and invalid-layout tests.
- `tests/LMSupply.Generator.Tests/LocalGeneratorDefaultRoutingTests.cs` — verify `"default"` goes through the auto path via a `TraceListener`.

---

## Commit 1 — ONNX path resolution bug fix

### Task 1: Add failing tests for 2-level nested variant path resolution

**Files:**
- Create: `tests/LMSupply.Generator.Tests/OnnxGeneratorPathResolutionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create the file with this content:

```csharp
using FluentAssertions;

namespace LMSupply.Generator.Tests;

/// <summary>
/// Regression tests for OnnxGeneratorModelFactory path resolution,
/// including the 2-level nested variant layout used by Phi-4 Mini ONNX.
/// </summary>
public class OnnxGeneratorPathResolutionTests
{
    private static string CreateHfCacheLayout(
        string root,
        string org,
        string name,
        string? variantRelativePath,
        bool includeConfig = true,
        bool includeModelOnnx = true)
    {
        var snapshotDir = Path.Combine(root, $"models--{org}--{name}", "snapshots", "main");
        var modelDir = variantRelativePath is null
            ? snapshotDir
            : Path.Combine(snapshotDir, variantRelativePath);
        Directory.CreateDirectory(modelDir);
        if (includeConfig)
            File.WriteAllText(Path.Combine(modelDir, "genai_config.json"), "{}");
        if (includeModelOnnx)
            File.WriteAllText(Path.Combine(modelDir, "model.onnx"), "dummy");
        return snapshotDir;
    }

    [Fact]
    public void IsModelAvailable_OneLevelVariant_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            CreateHfCacheLayout(tempDir, "acme", "test-model", "cpu-int4");
            using var factory = new OnnxGeneratorModelFactory(tempDir, ExecutionProvider.Cpu);

            factory.IsModelAvailable("acme/test-model").Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsModelAvailable_TwoLevelNestedVariant_ReturnsTrue()
    {
        // Phi-4 Mini layout: cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            CreateHfCacheLayout(
                tempDir,
                "microsoft",
                "Phi-4-mini-instruct-onnx",
                Path.Combine("cpu_and_mobile", "cpu-int4-rtn-block-32-acc-level-4"));

            using var factory = new OnnxGeneratorModelFactory(tempDir, ExecutionProvider.Cpu);

            factory.IsModelAvailable("microsoft/Phi-4-mini-instruct-onnx").Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsModelAvailable_NoValidLayout_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            // Directory exists but no genai_config.json anywhere
            var snapshot = Path.Combine(tempDir, "models--acme--empty", "snapshots", "main", "something");
            Directory.CreateDirectory(snapshot);

            using var factory = new OnnxGeneratorModelFactory(tempDir, ExecutionProvider.Cpu);

            factory.IsModelAvailable("acme/empty").Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsModelAvailable_SnapshotRoot_ReturnsTrue()
    {
        // Layout where genai_config.json sits at the snapshot root directly.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            CreateHfCacheLayout(tempDir, "acme", "flat-model", variantRelativePath: null);

            using var factory = new OnnxGeneratorModelFactory(tempDir, ExecutionProvider.Cpu);

            factory.IsModelAvailable("acme/flat-model").Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify the 2-level test fails**

Run:
```bash
dotnet test tests/LMSupply.Generator.Tests --filter "FullyQualifiedName~OnnxGeneratorPathResolutionTests" --nologo
```

Expected: `IsModelAvailable_TwoLevelNestedVariant_ReturnsTrue` FAILS (returns `false`). The other three pass. This is the regression the bug fix targets.

- [ ] **Step 3: Commit the failing test**

```bash
git add tests/LMSupply.Generator.Tests/OnnxGeneratorPathResolutionTests.cs
git commit -m "test(generator): add path resolution regression tests including 2-level nested variant"
```

### Task 2: Implement 2-level recursion in FindVariantSubfolder

**Files:**
- Modify: `src/LMSupply.Generator/OnnxGeneratorModelFactory.cs:256-288`

- [ ] **Step 1: Replace the `FindVariantSubfolder` method**

Locate the existing method (starts at line 256 with `private string? FindVariantSubfolder(string basePath)`) and replace it with the following:

```csharp
private string? FindVariantSubfolder(string basePath)
{
    if (!Directory.Exists(basePath))
        return null;

    // Provider-specific variant prefixes in priority order
    var variantPatterns = _defaultProvider switch
    {
        ExecutionProvider.Cuda => new[] { "cuda", "gpu", "cpu" },
        ExecutionProvider.DirectML => new[] { "directml", "gpu", "cpu" },
        _ => new[] { "cpu", "gpu" }
    };

    // Level 1: direct prefix match on an immediate subdirectory
    foreach (var pattern in variantPatterns)
    {
        foreach (var subdir in Directory.GetDirectories(basePath))
        {
            var dirName = Path.GetFileName(subdir);
            if (dirName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)
                && IsValidModelDirectory(subdir))
                return subdir;
        }
    }

    // Level 2: nested variant layout (e.g., cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4)
    // Walk into any level-1 directory whose name contains the provider pattern,
    // then check each of its immediate children for a valid model directory.
    foreach (var pattern in variantPatterns)
    {
        foreach (var subdir in Directory.GetDirectories(basePath))
        {
            var dirName = Path.GetFileName(subdir);
            if (!dirName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var nested in Directory.GetDirectories(subdir))
            {
                if (IsValidModelDirectory(nested))
                    return nested;
            }
        }
    }

    // Fallback: any valid model at level 1
    foreach (var subdir in Directory.GetDirectories(basePath))
    {
        if (IsValidModelDirectory(subdir))
            return subdir;
    }

    // Fallback: any valid model at level 2
    foreach (var subdir in Directory.GetDirectories(basePath))
    {
        foreach (var nested in Directory.GetDirectories(subdir))
        {
            if (IsValidModelDirectory(nested))
                return nested;
        }
    }

    return null;
}
```

- [ ] **Step 2: Build to verify compilation**

Run:
```bash
dotnet build src/LMSupply.Generator/LMSupply.Generator.csproj --nologo
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Run the path-resolution tests**

Run:
```bash
dotnet test tests/LMSupply.Generator.Tests --filter "FullyQualifiedName~OnnxGeneratorPathResolutionTests" --nologo
```

Expected: all four tests pass.

- [ ] **Step 4: Run the full Generator test project to guard against regressions**

Run:
```bash
dotnet test tests/LMSupply.Generator.Tests --filter "Category!=Integration" --nologo
```

Expected: all tests pass.

### Task 3: Add per-branch Trace logs and improve the FileNotFoundException message

**Files:**
- Modify: `src/LMSupply.Generator/OnnxGeneratorModelFactory.cs:214-251`

- [ ] **Step 1: Add the `System.Diagnostics` using directive**

At the top of `src/LMSupply.Generator/OnnxGeneratorModelFactory.cs`, ensure `using System.Diagnostics;` is present. If it isn't, add it below the existing `using` directives:

```csharp
using System.Diagnostics;
using LMSupply.Core.Download;
using LMSupply.Download;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.ChatFormatters;
```

- [ ] **Step 2: Replace the `ResolveModelPathWithBaseAsync` method body**

Locate the method (starts at line 214) and replace its body with:

```csharp
private async Task<(string modelPath, string? configBasePath)> ResolveModelPathWithBaseAsync(
    string modelId, CancellationToken cancellationToken)
{
    var snapshotPath = GetModelCachePath(modelId);
    var triedPaths = new List<string> { snapshotPath };

    // Branch (A): snapshot root
    if (IsValidModelDirectory(snapshotPath))
    {
        Trace.TraceInformation(
            $"[OnnxGenerator] Path resolution: snapshot-root hit for {modelId} at {snapshotPath}");
        return (snapshotPath, null);
    }

    // Branch (B): registry-driven subfolder
    GeneratorModelRegistry.Default.TryResolve(modelId, out var registryInfo);
    if (registryInfo?.Subfolder != null)
    {
        var registrySubfolderPath = Path.Combine(snapshotPath, registryInfo.Subfolder);
        triedPaths.Add(registrySubfolderPath);
        if (IsValidModelDirectory(registrySubfolderPath))
        {
            Trace.TraceInformation(
                $"[OnnxGenerator] Path resolution: registry-subfolder hit for {modelId} — {registryInfo.Subfolder}");
            return (registrySubfolderPath, snapshotPath);
        }
        Trace.TraceInformation(
            $"[OnnxGenerator] Path resolution: registry-subfolder MISS for {modelId} — tried {registrySubfolderPath}");
    }
    else
    {
        Trace.TraceInformation(
            $"[OnnxGenerator] Path resolution: no registry subfolder for {modelId} " +
            $"(registryInfo={(registryInfo == null ? "null" : "no-subfolder")})");
    }

    // Branch (C): variant subfolder search (1-level + 2-level)
    var foundPath = FindVariantSubfolder(snapshotPath);
    if (foundPath != null)
    {
        Trace.TraceInformation(
            $"[OnnxGenerator] Path resolution: variant-search hit for {modelId} — {Path.GetRelativePath(snapshotPath, foundPath)}");
        return (foundPath, snapshotPath);
    }

    // Model not found — attempt download and retry
    Trace.TraceInformation($"[OnnxGenerator] Path resolution: no cached layout found, attempting download for {modelId}");
    await DownloadModelAsync(modelId, null, cancellationToken);

    if (IsValidModelDirectory(snapshotPath))
        return (snapshotPath, null);

    foundPath = FindVariantSubfolder(snapshotPath);
    if (foundPath != null)
        return (foundPath, snapshotPath);

    throw new FileNotFoundException(
        $"Model '{modelId}' not found. Tried:\n  " +
        string.Join("\n  ", triedPaths) +
        "\nSearched variant subfolders (2 levels deep) — no directory containing 'genai_config.json' was found.");
}
```

- [ ] **Step 3: Build to verify compilation**

Run:
```bash
dotnet build src/LMSupply.Generator/LMSupply.Generator.csproj --nologo
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Re-run all Generator unit tests**

Run:
```bash
dotnet test tests/LMSupply.Generator.Tests --filter "Category!=Integration" --nologo
```

Expected: all tests pass.

### Task 4: Commit the bug fix

- [ ] **Step 1: Stage and commit**

```bash
git add src/LMSupply.Generator/OnnxGeneratorModelFactory.cs
git commit -m "fix(generator): resolve 2-level nested ONNX variant paths

FindVariantSubfolder now walks two directory levels so the HuggingFace
Phi-4 Mini layout (cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/)
is discovered when the registry subfolder lookup misses.

Adds per-branch Trace logs in ResolveModelPathWithBaseAsync so future
path regressions are diagnosable from a single log line, and augments
the FileNotFoundException with the list of attempted paths."
```

---

## Commit 2 — `"default"` alias routes through `"auto"`

### Task 5: Write the default-routing test

**Files:**
- Create: `tests/LMSupply.Generator.Tests/LocalGeneratorDefaultRoutingTests.cs`

- [ ] **Step 1: Write the failing test**

Create the file with this content:

```csharp
using System.Diagnostics;
using FluentAssertions;

namespace LMSupply.Generator.Tests;

/// <summary>
/// Verifies that LocalGenerator.LoadAsync("default") delegates to the
/// auto hardware-aware selection path. We detect the delegation by
/// listening for the "[LocalGenerator.auto]" Trace line emitted by
/// LoadAutoAsync, without actually downloading any model weights.
/// </summary>
public class LocalGeneratorDefaultRoutingTests
{
    private sealed class CapturingListener : TraceListener
    {
        private readonly List<string> _lines = new();
        public IReadOnlyList<string> Lines => _lines;

        public override void Write(string? message) { if (message != null) _lines.Add(message); }
        public override void WriteLine(string? message) { if (message != null) _lines.Add(message); }
    }

    private static async Task<IReadOnlyList<string>> CaptureTraceForLoadAsync(string modelId)
    {
        var listener = new CapturingListener();
        Trace.Listeners.Add(listener);
        try
        {
            try
            {
                // We don't care if the load fails after selection — we only
                // need the Trace line emitted by LoadAutoAsync before any
                // network I/O happens. Use a short cancellation to bail fast.
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                await LocalGenerator.LoadAsync(modelId, cancellationToken: cts.Token);
            }
            catch
            {
                // Expected: cancellation, network failure, or path resolution error.
            }
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
        return listener.Lines;
    }

    [Fact]
    public async Task LoadAsync_Default_EmitsAutoTraceLine()
    {
        var lines = await CaptureTraceForLoadAsync("default");
        lines.Should().Contain(l => l.Contains("[LocalGenerator.auto]"));
    }

    [Fact]
    public async Task LoadAsync_Auto_EmitsAutoTraceLine()
    {
        var lines = await CaptureTraceForLoadAsync("auto");
        lines.Should().Contain(l => l.Contains("[LocalGenerator.auto]"));
    }

    [Fact]
    public async Task LoadAsync_ExplicitRepoId_DoesNotEmitAutoTraceLine()
    {
        var lines = await CaptureTraceForLoadAsync("microsoft/Phi-4-mini-instruct-onnx");
        lines.Should().NotContain(l => l.Contains("[LocalGenerator.auto]"));
    }

    [Fact]
    public void FastAlias_StillResolvesToPhi4Mini()
    {
        var registry = GeneratorModelRegistry.Default;
        var resolved = registry.TryResolve("fast", out var info);

        resolved.Should().BeTrue();
        info.Should().NotBeNull();
        info!.ModelId.Should().Be("microsoft/Phi-4-mini-instruct-onnx");
    }

    [Fact]
    public void PhiMiniAlias_ResolvesToPhi4Mini()
    {
        var registry = GeneratorModelRegistry.Default;
        var resolved = registry.TryResolve("phi-4-mini", out var info);

        resolved.Should().BeTrue();
        info.Should().NotBeNull();
        info!.ModelId.Should().Be("microsoft/Phi-4-mini-instruct-onnx");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test tests/LMSupply.Generator.Tests --filter "FullyQualifiedName~LocalGeneratorDefaultRoutingTests" --nologo
```

Expected failures:
- `LoadAsync_Default_EmitsAutoTraceLine` — no `[LocalGenerator.auto]` line emitted because "default" routes through the ONNX path today.
- `LoadAsync_Auto_EmitsAutoTraceLine` — fails until the Trace line is added to `LoadAutoAsync`.
- `PhiMiniAlias_ResolvesToPhi4Mini` — fails because the `"phi-4-mini"` alias doesn't exist yet.

Other tests may pass. Note the failures before continuing.

### Task 6: Redirect `"default"` to `LoadAutoAsync` and emit Trace log

**Files:**
- Modify: `src/LMSupply.Generator/LocalGenerator.cs`

- [ ] **Step 1: Remove the `DefaultModel` constant and update `LoadAsync`**

Replace the `DefaultModel` const and the `LoadAsync` method. Locate lines 12-16 (the `DefaultModel` XML doc + const) and lines 45-84 (`LoadAsync`).

**Delete** the const block (lines 12-16):

```csharp
    /// <summary>
    /// Default model to use when no model is specified.
    /// Microsoft Phi-4 Mini (MIT license), 3.8B params, 16K context.
    /// </summary>
    public const string DefaultModel = "microsoft/Phi-4-mini-instruct-onnx";
```

**Replace** the `LoadAsync` body (keep the XML doc, replace only the method) with:

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

        // "default" and "auto" both delegate to hardware-aware selection.
        if (modelId.Equals("default", StringComparison.OrdinalIgnoreCase) ||
            modelId.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return LoadAutoAsync(options, progress, cancellationToken);
        }

        // Handle other standard aliases via the registry
        if (GeneratorModelRegistry.Default.TryResolve(modelId, out var resolvedModel))
        {
            modelId = resolvedModel!.ModelId;
        }

        // Check if it's a local file path (e.g., C:\models\model.gguf or /path/to/model.gguf)
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
```

- [ ] **Step 2: Update `LoadDefaultAsync` to call `LoadAsync("default", ...)`**

Replace the `LoadDefaultAsync` method body (lines 116-122 in the original file) with:

```csharp
    public static Task<IGeneratorModel> LoadDefaultAsync(
        GeneratorOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return LoadAsync("default", options, progress, cancellationToken);
    }
```

- [ ] **Step 3: Add Trace log to `LoadAutoAsync`**

Replace the `LoadAutoAsync` method body (starting at line 129) with:

```csharp
    private static Task<IGeneratorModel> LoadAutoAsync(
        GeneratorOptions options,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var profile = HardwareProfile.Current;
        var useOnnx = profile.RecommendedProvider == ExecutionProvider.DirectML &&
                      profile.GpuInfo.Vendor != GpuVendor.Nvidia;

        string selectedModelId;
        string selectedFormat;

        if (useOnnx)
        {
            var model = GeneratorModelRegistry.Default.Resolve("auto");
            selectedModelId = model.ModelId;
            selectedFormat = "ONNX";
            LogAutoSelection(profile, selectedFormat, selectedModelId);
            return Internal.GeneratorModelLoader.LoadAsync(
                selectedModelId, options, progress, cancellationToken);
        }
        else
        {
            var model = Internal.Llama.GgufModelRegistry.GetAutoModel();
            selectedModelId = model.RepoId;
            selectedFormat = "GGUF";
            LogAutoSelection(profile, selectedFormat, selectedModelId);
            return Internal.GeneratorModelLoader.LoadAsync(
                selectedModelId, options, progress, cancellationToken);
        }
    }

    private static void LogAutoSelection(HardwareProfile profile, string format, string modelId)
    {
        var vramMb = VramBudget.GetAvailableBytes(profile.GpuInfo) / (1024 * 1024);
        System.Diagnostics.Trace.TraceInformation(
            $"[LocalGenerator.auto] Provider={profile.RecommendedProvider}, " +
            $"GPU={profile.GpuInfo.Vendor} {profile.GpuInfo.Name ?? "n/a"}, " +
            $"VRAM={vramMb}MB → {format} path, selected={modelId}");
    }
```

- [ ] **Step 4: Build to verify compilation**

Run:
```bash
dotnet build src/LMSupply.Generator/LMSupply.Generator.csproj --nologo
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

### Task 7: Re-point Phi-4 Mini alias from `"default"` to `"phi-4-mini"`

**Files:**
- Modify: `src/LMSupply.Generator/Models/DefaultGeneratorModels.cs:29`

- [ ] **Step 1: Change the `AliasName` on `Phi4Mini`**

Find the `Phi4Mini` property (line 26 onward) and change the `AliasName` line from:

```csharp
        AliasName = "default",
```

to:

```csharp
        AliasName = "phi-4-mini",
```

- [ ] **Step 2: Run the default-routing tests**

Run:
```bash
dotnet test tests/LMSupply.Generator.Tests --filter "FullyQualifiedName~LocalGeneratorDefaultRoutingTests" --nologo
```

Expected: all five tests pass.

- [ ] **Step 3: Run all non-integration tests**

Run:
```bash
dotnet test tests/LMSupply.Generator.Tests --filter "Category!=Integration" --nologo
```

Expected: all tests pass.

- [ ] **Step 4: Run the full solution unit-test suite**

Run:
```bash
dotnet test --filter "Category!=Integration" --nologo
```

Expected: all tests pass.

### Task 8: Commit the alias redirect

- [ ] **Step 1: Stage and commit**

```bash
git add src/LMSupply.Generator/LocalGenerator.cs src/LMSupply.Generator/Models/DefaultGeneratorModels.cs tests/LMSupply.Generator.Tests/LocalGeneratorDefaultRoutingTests.cs
git commit -m "feat(generator): route plain \"default\" through auto hardware selection

LocalGenerator.LoadAsync now delegates both \"default\" and \"auto\" to
LoadAutoAsync, so NVIDIA/CPU/macOS/Linux users get Gemma 4 GGUF
automatically. Windows DirectML + non-NVIDIA still falls back to Phi-4
Mini ONNX (now fixed by the 2-level path resolution).

Breaking changes:
- LocalGenerator.DefaultModel const removed — call LoadDefaultAsync()
  or pass an explicit repo ID instead.
- DefaultGeneratorModels.Phi4Mini.AliasName is now \"phi-4-mini\" so the
  plain alias \"default\" is free for the auto path.

Adds [LocalGenerator.auto] Trace line summarizing the selection."
```

---

## Commit 3 — Documentation refresh

### Task 9: Reorganize README Generator Models section

**Files:**
- Modify: `README.md:267-283` (Generator alias tables)
- Modify: `README.md:351` (Platform-based routing note)

- [ ] **Step 1: Replace the Generator Models section**

Find the block starting around line 265 with the header "### Generator" (or the closest surrounding header — search for `| \`default\` \| Phi-4-mini-instruct`). The tables at 267-283 look like:

```
| `default` | Phi-4-mini-instruct | 3.8B | 16K | MIT | Balanced reasoning |
| `fast` | Phi-4-mini-instruct | 3.8B | 16K | MIT | Same as default (smallest FC-capable) |
| `quality` | phi-4 | 14B | 16K | MIT | Best reasoning |
| `medium` | Phi-3.5-mini-instruct | 3.8B | 128K | MIT | Long context (legacy) |
```

Replace the ONNX alias table AND the Gemma 4 GGUF table block with this combined structure. **Insert a new "Platform-based defaults" table first**:

```markdown
**Platform-based defaults** (`default` and `auto` delegate to this matrix):

| Platform | Selected backend | Selected model |
|----------|------------------|----------------|
| Windows + NVIDIA | GGUF (llama.cpp CUDA) | Gemma 4 via `gguf:auto` (VRAM-aware) |
| Windows + AMD/Intel GPU | ONNX (DirectML) | Phi-4 Mini (MIT, FC-capable) |
| Windows / Linux CPU-only | GGUF (llama.cpp CPU) | Gemma 4 via `gguf:auto` (VRAM-aware) |
| Linux + NVIDIA | GGUF (llama.cpp CUDA) | Gemma 4 via `gguf:auto` |
| macOS (Apple Silicon) | GGUF (llama.cpp Metal) | Gemma 4 via `gguf:auto` |

> `LoadAsync("default")` and `LoadAsync("auto")` both route through this matrix. For explicit selection, use `gguf:*` aliases, ONNX aliases, or a direct HuggingFace repo ID.

**ONNX aliases** (recommended for Windows DirectML + non-NVIDIA):

| Alias | Model | Params | Context | License | Notes |
|-------|-------|--------|---------|---------|-------|
| `phi-4-mini` | Phi-4-mini-instruct | 3.8B | 16K | MIT | Smallest FC-capable ONNX model |
| `fast` | Phi-4-mini-instruct | 3.8B | 16K | MIT | Same as `phi-4-mini` |
| `quality` | phi-4 | 14B | 16K | MIT | Best reasoning |
| `phi-3.5-mini` | Phi-3.5-mini-instruct | 3.8B | 128K | MIT | Long context (legacy) |

**GGUF aliases** — Gemma 4 중심 레지스트리 (Apache 2.0, 멀티모달, 네이티브 function calling). llama.cpp **b8672+** 필요 — `gguf:fast`/`default`/`balanced`/`quality`/`large` 로딩 시 최소 버전이 자동 검증됩니다.

| Alias | Model | Params | Quant | Size | VRAM target |
|-------|-------|--------|-------|------|-------------|
| `gguf:fast` | Gemma 4 E2B Instruct | 2.3B | Q4_K_M | ~3.1 GB | <4GB iGPU/mobile |
| `gguf:default` | Gemma 4 E4B Instruct | 4.5B | Q4_K_M | ~5.3 GB | 4-8GB |
| `gguf:balanced` | Gemma 4 E4B Instruct | 4.5B | Q8_0 | ~7.5 GB | 8-16GB (RTX 3060 12GB 등) |
| `gguf:quality` | Gemma 4 26B A4B (MoE) | 26B (4B active) | Q4_K_M | ~16.8 GB | 16-20GB |
| `gguf:large` | Gemma 4 31B Instruct | 31B | Q4_K_M | ~18.7 GB | 20-48GB |
```

- [ ] **Step 2: Update the Platform-based routing note**

Find the line around 351 starting with `> **Platform-based routing (v0.21.0+):**` and replace the entire paragraph with:

```markdown
> **Platform-based routing (v0.28.0+):** `LoadAsync("default")` and `LoadAsync("auto")` both select the optimal backend+model for the current host: GGUF via llama.cpp on CPU / NVIDIA / Apple Silicon / Linux, and ONNX via DirectML on Windows AMD/Intel. Use `gguf:*` aliases or ONNX aliases for explicit control.
```

- [ ] **Step 3: Preview the README to verify formatting**

Run:
```bash
grep -n "Platform-based defaults\|Platform-based routing\|gguf:default\|phi-4-mini" README.md | head -20
```

Expected: the new labels appear in the output.

### Task 10: Mirror the matrix in `docs/generator.md`

**Files:**
- Modify: `docs/generator.md` (two edits — comment updates + new section before "## Model Selection")

- [ ] **Step 1: Update the `WithDefaultModel()` comment**

The file currently contains two `WithDefaultModel()` code examples. Both have the comment `// Uses Phi-4 Mini (ONNX)`. Update each to read `// Uses the platform-appropriate default (see "Default and auto selection" below)`.

First instance (near the top, inside the "### Simple Text Generation" block):

Replace:
```csharp
    .WithDefaultModel()        // Uses Phi-4 Mini (ONNX)
```

with:
```csharp
    .WithDefaultModel()        // Platform-aware: Gemma 4 GGUF on NVIDIA/CPU/Mac/Linux, Phi-4 Mini ONNX on DirectML+non-NVIDIA
```

Second instance (inside the "### Chat Completion" block) — replace:
```csharp
    .WithDefaultModel()
    .BuildAsync();
```
with:
```csharp
    .WithDefaultModel()  // see "Default and auto selection" below
    .BuildAsync();
```

- [ ] **Step 2: Insert a "Default and auto selection" section directly before "## Model Selection"**

Locate the `## Model Selection` header. Immediately **before** it, insert:

````markdown
## Default and auto selection

`LocalGenerator.LoadAsync("default")` and `LocalGenerator.LoadAsync("auto")` both delegate to a hardware-aware selection:

| Platform | Backend | Model |
|----------|---------|-------|
| NVIDIA GPU (any OS) | GGUF (llama.cpp CUDA) | Gemma 4 via `gguf:auto` (VRAM-aware) |
| Apple Silicon | GGUF (llama.cpp Metal) | Gemma 4 via `gguf:auto` |
| CPU-only (any OS) | GGUF (llama.cpp CPU) | Gemma 4 via `gguf:auto` |
| Linux + any GPU | GGUF (llama.cpp) | Gemma 4 via `gguf:auto` |
| Windows + AMD/Intel GPU | ONNX (DirectML) | Phi-4 Mini (FC-capable, MIT) |

### Explicit model selection

```csharp
// Pin a specific ONNX model (DirectML + non-NVIDIA users)
await using var onnx = await LocalGenerator.LoadAsync("microsoft/Phi-4-mini-instruct-onnx");
await using var onnxAlias = await LocalGenerator.LoadAsync("phi-4-mini");

// Pin a specific GGUF model
await using var gguf = await LocalGenerator.LoadAsync("gguf:default"); // Gemma 4 E4B
await using var ggufXL = await LocalGenerator.LoadAsync("gguf:large"); // Gemma 4 31B

// Let the hardware decide (GGUF on most platforms, ONNX on DirectML+non-NVIDIA)
await using var auto = await LocalGenerator.LoadAsync("auto");
await using var def  = await LocalGenerator.LoadAsync("default"); // same as "auto"
```

````

### Task 11: Add v0.28.0 entry to `ROADMAP.md`

**Files:**
- Modify: `ROADMAP.md` (replace the "Current" header and insert a new Highlights block)

- [ ] **Step 1: Replace the Current-version header**

Find the line `## ✅ Version 0.26.x (Current)` and replace it with `## ✅ Version 0.28.x (Current)`.

- [ ] **Step 2: Replace the Theme line**

Find the line immediately below the header:
```markdown
**Theme**: Gemma 4 Native Support & Multimodal Foundations
```

Replace with:
```markdown
**Theme**: Platform-aware Default Alias & ONNX Path Hardening
```

- [ ] **Step 3: Prepend new v0.28.0 highlights**

Inside the `### Highlights` bullet list of the same section, insert these three bullets at the **top** (above the existing `- **Gemma 4 Chat Format** …` line) — keep the existing bullets in place so the 0.26.x accomplishments remain visible under the renamed header:

```markdown
- **Platform-aware `default` alias** (v0.28.0): `LocalGenerator.LoadAsync("default")` now delegates to `"auto"` — Gemma 4 GGUF is selected on NVIDIA/CPU/macOS/Linux; Windows DirectML + non-NVIDIA still routes to Phi-4 Mini ONNX.
- **ONNX path resolution fix** (v0.28.0): `FindVariantSubfolder` now walks two directory levels so Phi-4 Mini's `cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/` layout resolves on first run from a clean HuggingFace cache.
- **Auto-selection diagnostics** (v0.28.0): `[LocalGenerator.auto]` Trace line reports the selected backend, model, GPU, and VRAM budget.
```

- [ ] **Step 4: Append a Breaking Changes callout**

Immediately after the `### Completed Cycles` table, add:

```markdown
### Breaking changes (v0.28.0)

- `LocalGenerator.DefaultModel` const removed. Use `LoadDefaultAsync()` or pass an explicit repo ID.
- `DefaultGeneratorModels.Phi4Mini.AliasName` changed from `"default"` to `"phi-4-mini"`. The plain `"default"` alias now resolves through the platform-aware auto path.
```

### Task 12: Commit the documentation refresh

- [ ] **Step 1: Stage and commit**

```bash
git add README.md docs/generator.md ROADMAP.md
git commit -m "docs: reorganize generator alias tables and add platform matrix

Replaces the single \"default = Phi-4 Mini\" entry with a platform-based
defaults matrix at the top of the generator section, followed by
separate ONNX and GGUF explicit-alias tables. Mirrors the matrix in
docs/generator.md and records the v0.28.0 release in ROADMAP."
```

---

## Commit 4 — Version bump

### Task 13: Bump version to 0.28.0

**Files:**
- Modify: `Directory.Build.props`

- [ ] **Step 1: Change the `<Version>` value**

Locate the line `<Version>0.27.4</Version>` and replace with:

```xml
<Version>0.28.0</Version>
```

- [ ] **Step 2: Build the full solution to verify**

Run:
```bash
dotnet build --nologo
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit the version bump**

```bash
git add Directory.Build.props
git commit -m "chore: bump version to 0.28.0 for default alias reroute"
```

---

## Final verification

- [ ] **Step 1: Run the full unit-test suite end-to-end**

```bash
dotnet test --filter "Category!=Integration" --nologo
```

Expected: every test project passes.

- [ ] **Step 2: Confirm commit history**

```bash
git log --oneline -6
```

Expected output (order may vary by merge order but the four commits should appear consecutively on top of `da94849`):

```
<hash4> chore: bump version to 0.28.0 for default alias reroute
<hash3> docs: reorganize generator alias tables and add platform matrix
<hash2> feat(generator): route plain "default" through auto hardware selection
<hash1> fix(generator): resolve 2-level nested ONNX variant paths
<test>  test(generator): add path resolution regression tests including 2-level nested variant
da94849 docs(spec): generator default alias platform-aware routing + ONNX path fix
```

- [ ] **Step 3: Stop here**

Do **not** push to `origin/main` or open a PR. The user will decide whether to merge locally or push after reviewing the branch.
