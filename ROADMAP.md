# LMSupply Roadmap

> **Philosophy**: "No model management. Just use it."
>
> 모든 개선은 이 철학을 강화하는 방향으로 진행됩니다.

---

## ✅ Version 0.26.x (Current)

**Theme**: Gemma 4 Native Support & Multimodal Foundations

### Highlights

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

**Theme**: Vision Pipeline Completion

### Planned

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
| 0.26.x | Gemma 4 & Multimodal Foundations | **Current** |
