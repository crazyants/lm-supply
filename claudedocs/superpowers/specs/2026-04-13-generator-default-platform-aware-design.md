# Generator Default Alias: Platform-aware Routing + ONNX Path Fix

**Date**: 2026-04-13
**Target version**: v0.28.0
**Status**: Draft → pending user review

## 1. Problem

Two independent problems surfaced through a downstream (filer-ai) triage report at `D:\data\Filer\claudedocs\upstream-issues\lm-supply-phi4-onnx-path-resolution.md`:

1. **Root bug** — `OnnxGeneratorModelFactory.FindVariantSubfolder` only walks one directory level. HuggingFace's Phi-4 Mini ONNX release stores `genai_config.json` two levels deep (`cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/`). When the registry-driven branch silently misses, resolution fails with a `FileNotFoundException` that does not explain which paths were tried.
2. **Default alias mismatch** — The plain alias `"default"` still resolves to `Phi-4 Mini ONNX`, even though the project has pivoted toward Gemma 4 GGUF as the recommended local generator (Apache 2.0, native tool calling, multimodal foundations). `LocalGenerator.LoadAsync("default")` therefore delivers a model whose runtime is buggier and whose license/ergonomics are strictly worse for most users.

Upstream constraint: Gemma 4 ONNX weights exist at `onnx-community/gemma-4-*-ONNX`, but `Microsoft.ML.OnnxRuntimeGenAI 0.12.2` cannot execute them yet (PLE, variable head dims, KV cache sharing — `microsoft/onnxruntime-genai#2062`). A "Gemma 4 everywhere via ONNX" switch is blocked until the runtime ships support.

## 2. Goals

- Make `"default"` mean "the right thing for this machine" — aligning with lm-supply's core philosophy of on-demand local inference tuned to the host.
- Fix the 2-level nested ONNX path resolution bug so Phi-4 Mini and other deep-layout ONNX models load on first run.
- Improve observability so future path/alias regressions are diagnosable from a single log line.
- Preserve explicit opt-in paths (`"gguf:default"`, explicit repo IDs, `"quality"`, `"fast"`) unchanged.

## 3. Non-goals

- Changing `auto` selection heuristics themselves (current NVIDIA/DirectML/CPU matrix is sound).
- Introducing a `PreviewAuto()` diagnostic API (YAGNI — Trace logging suffices).
- Waiting for `onnxruntime-genai` to add Gemma 4 support before shipping this change.
- Adjusting GGUF or ONNX model registries' content (Phi-4 tier stays available under explicit aliases).

## 4. Design

### 4.1 `"default"` alias routing

`LocalGenerator.LoadAsync(modelId, …)` gains an early branch:

```csharp
if (modelId.Equals("default", StringComparison.OrdinalIgnoreCase) ||
    modelId.Equals("auto",    StringComparison.OrdinalIgnoreCase))
{
    return LoadAutoAsync(options, progress, cancellationToken);
}
```

`LoadDefaultAsync()` is rewritten to call `LoadAsync("default", …)` (previously called `LoadAsync(DefaultModel, …)`).

`DefaultGeneratorModels.Phi4Mini.AliasName` changes from `"default"` to `"phi-4-mini"` so users can still pin the model explicitly. The `"fast"` system alias at `GeneratorModelRegistry.cs:40` remains pointed at Phi-4 Mini because it remains the smallest FC-capable ONNX model.

`LocalGenerator.DefaultModel` public const is **removed** (CLAUDE.md Breaking Change Policy: prefer direct refactoring over `[Obsolete]` deprecation).

### 4.2 `auto` routing — preserve, observe

No logic change to `LoadAutoAsync`. Add one `Trace.TraceInformation` line recording:

- `RecommendedProvider`
- GPU vendor + name
- Available VRAM (MB)
- Selected format (ONNX / GGUF)
- Selected model ID

Format:

```
[LocalGenerator.auto] Provider={provider}, GPU={vendor} {name}, VRAM={mb}MB → {ONNX|GGUF} path, selected={modelId}
```

### 4.3 ONNX path resolution (`FindVariantSubfolder`)

Add level-2 traversal. Depth cap is 2 (no unbounded recursion). Logic:

1. Level-1 prefix match (existing behavior).
2. Level-2 prefix match — for any level-1 directory whose name contains/starts with a provider pattern, check its immediate children for `IsValidModelDirectory`.
3. Level-1 any-valid fallback (existing).
4. Level-2 any-valid fallback (new).

The containment check for level-1 covers HuggingFace's `cpu_and_mobile/` pattern (where "cpu" is embedded, not a prefix).

### 4.4 Path-resolution observability

`ResolveModelPathWithBaseAsync` gains per-branch Trace logs (Information level):

- Snapshot root hit
- Registry subfolder hit / miss (with path tried)
- Variant subfolder search hit (with relative path)
- Final `FileNotFoundException` includes the list of attempted paths.

### 4.5 Error message improvement

```csharp
throw new FileNotFoundException(
    $"Model '{modelId}' not found. Tried:\n  " +
    string.Join("\n  ", triedPaths) +
    "\nSearched variant subfolders (2 levels deep) — no directory containing 'genai_config.json' was found.");
```

### 4.6 Documentation updates

- **README.md:267-283** — Restructure Generator Models section:
  1. New "Platform-based defaults" matrix (default/auto → ONNX or GGUF by platform).
  2. Existing GGUF alias table preserved.
  3. Existing ONNX alias table preserved with "DirectML + non-NVIDIA recommended" label.
- **README.md:351** — Update "Platform-based routing" note to reflect that `default` now also flows through `auto`.
- **docs/generator.md** — Mirror the README matrix; add "Explicit model selection" example set (`"microsoft/Phi-4-mini-instruct-onnx"`, `"gguf:default"`, `"phi-4-mini"`).
- **ROADMAP.md** — Add v0.28.0 entry: "default alias now platform-aware; Gemma 4 is the de facto default on NVIDIA/CPU/macOS/Linux".

### 4.7 Versioning

`Directory.Build.props`: `0.27.4` → `0.28.0`. Breaking changes under v0.x trigger a minor bump per project convention.

### 4.8 Migration guide (release notes)

```markdown
## v0.28.0 Breaking Changes

### `LocalGenerator.DefaultModel` constant removed
Use `LocalGenerator.LoadDefaultAsync()` instead:

  ❌ await LocalGenerator.LoadAsync(LocalGenerator.DefaultModel);
  ✅ await LocalGenerator.LoadDefaultAsync();
  ✅ await LocalGenerator.LoadAsync("microsoft/Phi-4-mini-instruct-onnx"); // explicit

### `"default"` alias now platform-aware
`LoadAsync("default")` and `LoadDefaultAsync()` now delegate to `"auto"`:
- NVIDIA / CPU / macOS / Linux → Gemma 4 GGUF (Apache 2.0, native tool calling)
- Windows + AMD/Intel DirectML → Phi-4 Mini ONNX

To pin the previous behavior, call with the explicit repo ID:
  await LocalGenerator.LoadAsync("microsoft/Phi-4-mini-instruct-onnx");
```

## 5. Test plan

New test file: `tests/LMSupply.Generator.Tests/OnnxGeneratorPathResolutionTests.cs`

1. **1-level variant layout** — `snapshot/cpu-int4/genai_config.json` resolves.
2. **2-level variant layout** — `snapshot/cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/genai_config.json` resolves (regression guard for this issue).
3. **Registry-driven `Subfolder`** — when `ModelInfo.Subfolder` is set, branch (B) wins over fallback search.
4. **No valid layout** — `FileNotFoundException` is thrown and message contains the list of attempted paths.

Fixtures use `Path.GetTempPath()` and write empty `genai_config.json` / `model.onnx` files to simulate HuggingFace cache layouts. No network, no real model downloads.

New test file: `tests/LMSupply.Generator.Tests/LocalGeneratorDefaultRoutingTests.cs`

Since `LocalGenerator` is a static factory backed by `GeneratorModelLoader.LoadAsync`, these tests use a `TraceListener` to capture the `[LocalGenerator.auto] …` log line emitted by `LoadAutoAsync`, plus a test-visible hook (either an internal `Func<…>` override or `[InternalsVisibleTo]` exposure of `LoadAutoAsync`) to inspect the resolved model ID without actually downloading weights.

1. `LoadAsync("default")` emits the `[LocalGenerator.auto]` Trace line (proves it went through the auto path).
2. `LoadAsync("default")` and `LoadAsync("auto")` select the same model ID under an identical `HardwareProfile` override.
3. `LoadAsync("microsoft/Phi-4-mini-instruct-onnx")` does **not** emit the auto Trace line — it routes straight to the ONNX loader.
4. `GeneratorModelRegistry.Default.TryResolve("fast")` still returns Phi-4 Mini (no regression on the `fast` alias).

Existing `VramAwareSelectionIntegrationTests` must continue to pass without modification.

## 6. Commit/PR structure

The change is delivered as four focused commits on a feature branch:

1. `fix(generator): resolve 2-level nested ONNX variant paths` — `FindVariantSubfolder` 2-level recursion, per-branch Trace logs in `ResolveModelPathWithBaseAsync`, improved `FileNotFoundException` message, new path-resolution tests. Safe to ship in isolation as a patch if needed.
2. `feat(generator): route plain "default" through auto hardware selection` — alias redirect in `LocalGenerator.LoadAsync`, `DefaultModel` const removal, `Phi4Mini.AliasName` change to `"phi-4-mini"`, `[LocalGenerator.auto]` Trace line, default-routing tests.
3. `docs: reorganize generator alias tables and add platform matrix` — README, docs/generator.md, ROADMAP updates.
4. `chore: bump version to 0.28.0 for default alias reroute` — single-line version bump.

Each commit compiles and passes tests independently.

## 7. Rollback considerations

Commit (1) is independently rollback-safe (pure bug fix).
Commit (2) is the behavioral change; rolling it back restores Phi-4 Mini as plain `"default"` while leaving the path bug fixed.

## 8. Open questions / follow-up

- Monitor `microsoft/onnxruntime-genai#2062`. When Gemma 4 runtime support ships, reconsider adding a Gemma 4 ONNX entry to `DefaultGeneratorModels.All` and expanding the `auto` heuristic to prefer Gemma 4 on DirectML too.
- Downstream (filer-ai) can drop its `"gguf:default"` workaround once v0.28.0 lands.
