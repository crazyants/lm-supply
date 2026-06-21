# LMSupply Roadmap

> **Philosophy**: "No model management. Just use it."
>
> 모든 개선은 이 철학을 강화하는 방향으로 진행됩니다.

---

## ✅ Version 0.34.x (Current)

**Theme**: Environment-aware Model Selection & Thinking Control

### Highlights

- **Qwen3 registry & auto-pool** (0.34.13–25): Qwen3/3.5/3.6 tiers; `gguf:auto` restricted to the `qwen3-*` pool; quantization-aware low-end selection.
- **VRAM-budget gate** (0.34.23): low-VRAM / integrated GPU demotes the Auto GPU backend to CPU (no GPU layers would fit) instead of paying GPU init for zero offload.
- **RAM-aware selection + integrated-GPU detection** (0.34.24); **integrated GPU → GGUF routing** (0.34.27, not ONNX/DirectML).
- **VRAM-budget telemetry** on `GeneratorModelInfo` (0.34.22); `LMSUPPLY_SYSTEM_RAM_MB` / `LMSUPPLY_VRAM_BUDGET_MB` overrides + budget-aware cache reuse (0.34.26).
- **Auto provider CPU fallback on floored context** (0.34.21).
- **Unified `LlamaBackendSelector`** — generator/embedder/reranker pick the same backend for the same hardware.
- **ThinkingMode control** (0.34.28): `GenerationOptions.Thinking` ∈ `{Auto, On, Off}` replaces `EnableThinking`. `Off` forwards `enable_thinking=false` via `chat_template_kwargs` so a thinking-default-on model (Qwen3) answers directly; `Auto` (default) preserves each model's built-in behavior.

### Verification (2026-06-21, RTX 4060 dogfooding)

- NVML discrete-GPU detection (multi-GPU: RTX 4060 + Intel UHD → correct NVIDIA primary, 8GB not WMI-capped 4GB).
- env-override selection matrix (low-RAM / low-VRAM → demote + size-down) on the real `GetAutoSelection`/`LlamaBackendSelector` path.
- CUDA + forced-CPU-fallback E2E (load + generate) verified.

### Breaking changes (v0.34.28)

- `GenerationOptions.EnableThinking` (bool) removed → `GenerationOptions.Thinking` (`ThinkingMode` enum). Default `Auto` preserves prior behavior (Qwen3 thinks, Gemma does not).

---

## ✅ Version 0.28.x

**Theme**: Platform-aware Default Alias & ONNX Path Hardening

### Highlights

- **Platform-aware `default` alias** (v0.28.0): `LocalGenerator.LoadAsync("default")` now delegates to `"auto"` — Gemma 4 GGUF is selected on NVIDIA/CPU/macOS/Linux; Windows DirectML + non-NVIDIA still routes to Phi-4 Mini ONNX.
- **ONNX path resolution fix** (v0.28.0): `FindVariantSubfolder` now walks two directory levels so Phi-4 Mini's `cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/` layout resolves on first run from a clean HuggingFace cache.
- **Auto-selection diagnostics** (v0.28.0): `[LocalGenerator.auto]` Trace line reports the selected backend, model, GPU, and VRAM budget.
- **Gemma 4 Chat Format**: Native `system` role support via `Gemma4ChatFormatter`
- **llama-server Version Validation**: Architecture-aware minimum version checks (`b8672+` required for Gemma 4)
- **VRAM Gap Coverage**: New `gguf:balanced` alias (E4B Q8_0, 7.5GB) fills the 8-16GB VRAM range
- **Split GGUF Downloads**: `gguf:xlarge` (Qwen 3.5 122B, 3 shards) now downloads correctly
- **Multimodal `ChatMessage`**: Additive `ContentParts` API with `TextContentPart`/`ImageContentPart` for vision models
- **Backward Compatible**: All existing text-only `ChatMessage` usage unchanged

### Completed Cycles

| Cycle | Description | Status |
|-------|-------------|--------|
| **#1** | `Gemma4ChatFormatter` (native system role) | ✅ |
| **#2** | llama-server minimum version validation by architecture | ✅ |
| **#3** | `gguf:balanced` alias for 8-16GB VRAM gap | ✅ |
| **#4** | Split GGUF download support (`ShardCount`, `Q4_K_M/` subfolder) | ✅ |
| **#5** | `ChatMessage.ContentParts` multimodal model | ✅ |

Per-cycle logs: `claudedocs/cycle-logs/cycle-0{1..5}.md`

### Breaking changes (v0.28.0)

- `LocalGenerator.DefaultModel` const removed. Use `LoadDefaultAsync()` or pass an explicit repo ID.
- `DefaultGeneratorModels.Phi4Mini.AliasName` changed from `"default"` to `"phi-4-mini"`. The plain `"default"` alias now resolves through the platform-aware auto path.
- `WellKnownModels.Generator.Default` is now the alias `"default"` (was the Phi-4 Mini repo ID); `WellKnownModels.Generator.Fast` is now `"phi-4-mini"`.

---

## ✅ Version 0.10.0 (Released)

**Theme**: Local Performance Maximization & Developer Experience

### Highlights

- **HardwareProfile & PerformanceTier**: 통합 하드웨어 감지 시스템
- **"auto" Model Selection**: 하드웨어 기반 최적 모델 자동 선택
- **Runtime Diagnostics**: 모든 도메인에 `IsGpuActive`, `ActiveProviders`, `EstimatedMemoryBytes` 추가
- **IModelInfoBase**: 통합 모델 정보 인터페이스
- **Documentation**: MODEL_LIFECYCLE.md, GPU_PROVIDERS.md, MEMORY_REQUIREMENTS.md, TROUBLESHOOTING.md

---

## 🔮 Next Cycles (Planning)

### Handoff — open work (2026-06-21)

- [x] **gemma4-E2B empty chat response** at small token budget — CLOSED (2026-06-21, RTX 4060 probe). Root cause: reasoning consumes the 30-token budget (text=0, finish=length at Auto); `Thinking.Off` recovers, Auto@256 answers. Fix: doc + test only (no version bump) — `E_MultiTurnChat_Works`→`Thinking.Off`, new `Gemma4EmptyChatProbeTests` regression guards, `ThinkingMode`/`GenerationOptions.Thinking` doc accuracy. Issue → `closed/`.
- [x] **Integration test suite runtime green** — affected chat tests pass (`E_MultiTurnChat_Works` + siblings in isolation). Note: concurrent runs may flake with `HttpRequestException` (multiple llama-server instances contend on one 8GB GPU) — environmental, not a code regression.
- [ ] **gemma4 registry thinking metadata** (follow-up, needs E4B/26B verification) — gemma4-E2B behaves thinking-default-on, but `ThinkingEnabledByDefault` is advisory-only (never consumed for behavior) and only E2B is verified; adding the flag to gemma4 entries forces a family pack + version bump for zero runtime change. Defer until E4B/26B reasoning behavior is confirmed.
- [ ] **Filer field validation (dogfooding closes)**: `Thinking.Off` resolves ISSUE-223 thinking-burn (closes the EnableThinking issue); VRAM-budget telemetry (a)/(b) classification; low-VRAM ctx-clamp unbrick; single-delta streaming locus (filer-host live runtime).
- [x] **GgufModelRegistry XML doc** — stale `gguf:default`/`fast`/`quality` examples refreshed to registered `gguf:gemma4-*` aliases across 5 files (`691bb90`, 2026-06-21).

### Theme: Vision Pipeline Completion

- [ ] Wire `ContentParts` through `LlamaServerClient` (OpenAI vision JSON serialization)
- [ ] mmproj file auto-discovery and loading for Gemma 4 multimodal
- [ ] CUDA backend verification (NVIDIA GPU)
- [ ] First-token latency optimization
- [ ] KV cache quantization testing

---

## Version History

| Version | Theme | Status |
|---------|-------|--------|
| 0.9.2 | ONNX Runtime Management | Released |
| 0.10.0 | Local Performance Max & DX | Released |
| 0.26.x | Gemma 4 & Multimodal Foundations | Released |
| 0.28.x | Platform-aware Default Alias & ONNX Path Hardening | Released |
| 0.34.x | Environment-aware Model Selection & Thinking Control | **Current** |
