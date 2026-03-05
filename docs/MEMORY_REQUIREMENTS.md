# Memory Requirements Guide

> LMSupply 모델 메모리 요구사항 및 OOM 예방 가이드

---

## Overview

LMSupply의 모델은 ONNX Runtime을 통해 실행됩니다. 메모리 사용량은 다음 요소에 의해 결정됩니다:

- **모델 가중치(Weights)**: 모델 파일 크기의 ~1.5-2배
- **입력/출력 텐서**: 배치 크기와 시퀀스 길이에 비례
- **중간 활성화(Activations)**: 추론 중 생성되는 임시 데이터
- **런타임 오버헤드**: ONNX Runtime 내부 버퍼

### Memory Estimation Formula

```
EstimatedMemory = ModelFileSize × 2
```

이 공식은 모델 가중치 로딩과 런타임 오버헤드를 포함한 대략적인 추정치입니다.

---

## Memory by Domain

### Embedder (텍스트 임베딩)

| Model | Parameters | ONNX Size | Est. Memory | Context |
|-------|------------|-----------|-------------|---------|
| all-MiniLM-L6-v2 | 22M | ~90MB | ~180MB | 256 tokens |
| bge-small-en-v1.5 | 33M | ~130MB | ~260MB | 512 tokens |
| e5-small-v2 | 33M | ~130MB | ~260MB | 512 tokens |
| bge-base-en-v1.5 | 110M | ~440MB | ~880MB | 512 tokens |
| gte-base-en-v1.5 | 109M | ~440MB | ~880MB | 8K tokens |
| nomic-embed-text-v1.5 | 137M | ~550MB | ~1.1GB | 8K tokens |
| multilingual-e5-base | 278M | ~1.1GB | ~2.2GB | 512 tokens |
| bge-large-en-v1.5 | 335M | ~1.3GB | ~2.6GB | 512 tokens |
| gte-large-en-v1.5 | 434M | ~1.7GB | ~3.4GB | 8K tokens |
| multilingual-e5-large | 560M | ~2.2GB | ~4.4GB | 512 tokens |

**권장 선택**:
- 💡 **Low Memory (< 4GB)**: `fast` (all-MiniLM-L6-v2)
- ⚖️ **Balanced (4-8GB)**: `default` (bge-small-en-v1.5)
- 🚀 **Quality (8GB+)**: `quality` (gte-base-en-v1.5) or `large`

---

### Reranker (재순위화)

| Model | Parameters | ONNX Size | Est. Memory | Context |
|-------|------------|-----------|-------------|---------|
| ms-marco-TinyBERT-L-2 | 4.4M | ~18MB | ~36MB | 512 tokens |
| ms-marco-MiniLM-L-6 | 22M | ~90MB | ~180MB | 512 tokens |
| ms-marco-MiniLM-L-12 | 33M | ~134MB | ~270MB | 512 tokens |
| bge-reranker-base | 278M | ~440MB | ~880MB | 512 tokens |
| bge-reranker-large | 560M | ~1.1GB | ~2.2GB | 512 tokens |
| bge-reranker-v2-m3 | 568M | ~1.1GB | ~2.2GB | 8K tokens |

**권장 선택**:
- 💡 **Fast**: `ms-marco-TinyBERT-L-2` (초경량)
- ⚖️ **Default**: `ms-marco-MiniLM-L-6` (균형)
- 🚀 **Quality**: `bge-reranker-large`

---

### Generator (텍스트 생성)

Generator 모델은 다른 도메인보다 훨씬 큰 메모리를 요구합니다.

| Model | Parameters | ONNX Size | Est. Memory | Context |
|-------|------------|-----------|-------------|---------|
| Llama-3.2-1B-Instruct | 1B | ~2GB | ~4GB | 8K tokens |
| Gemma-2-2B-IT | 2B | ~4GB | ~8GB | 8K tokens |
| Qwen2.5-3B-Instruct | 3B | ~6GB | ~12GB | 128K tokens |
| Phi-3.5-mini-instruct | 3.8B | ~7.5GB | ~15GB | 128K tokens |
| Phi-4-mini-instruct | 3.8B | ~7.5GB | ~15GB | 16K tokens |
| Phi-4 | 14B | ~28GB | ~56GB | 16K tokens |

**GGUF 포맷 (양자화)**:

양자화된 GGUF 모델은 크게 줄어든 메모리를 사용합니다:

| Quantization | Memory Reduction | Quality Impact |
|--------------|-----------------|----------------|
| Q8_0 | ~50% | Minimal |
| Q6_K | ~60% | Very Low |
| Q5_K_M | ~65% | Low |
| Q4_K_M | ~75% | Moderate |
| Q3_K_M | ~80% | Noticeable |
| Q2_K | ~87% | Significant |

**자동 양자화 선택 (v0.16.0+)**:

LMSupply는 GGUF 레포에서 여러 양자화 파일 중 **현재 하드웨어에 맞는 최적 파일을 자동 선택**합니다:

- 사용 가능한 VRAM과 RAM을 측정 (VRAM - 2GB 오버헤드, RAM - 4GB 오버헤드)
- 메모리에 맞는 파일 중 가장 품질이 높은 양자화를 선택 (Q8 > Q6 > Q5 > Q4 ...)
- 특정 파일을 직접 지정하려면: `new GeneratorOptions { GgufFileName = "model-Q4_K_M.gguf" }`

**권장 선택**:
- 💡 **Low Memory (4-8GB)**: `Llama-3.2-1B` or GGUF Q4_K_M (자동 선택)
- ⚖️ **Balanced (8-16GB)**: `Phi-3.5-mini` or `Phi-4-mini`
- 🚀 **Quality (16GB+)**: `Phi-4-mini` ONNX or larger GGUF (자동 선택)

---

### Transcriber (음성 인식)

| Model | Parameters | ONNX Size | Est. Memory | Languages |
|-------|------------|-----------|-------------|-----------|
| Whisper Tiny | 39M | ~150MB | ~300MB | Multi |
| Whisper Base | 74M | ~280MB | ~560MB | Multi |
| Whisper Small | 244M | ~950MB | ~1.9GB | Multi |
| Whisper Medium | 769M | ~3GB | ~6GB | Multi |
| Whisper Large V3 | 1.5B | ~6GB | ~12GB | Multi |
| Whisper Large V3 Turbo | 809M | ~3.2GB | ~6.4GB | Multi |

**권장 선택**:
- 💡 **Fast**: `whisper-tiny-en` (영어 전용, 초고속)
- ⚖️ **Default**: `whisper-base` or `whisper-small`
- 🚀 **Quality**: `whisper-large-v3-turbo` (품질/속도 최적)

---

### Detector (객체 검출)

| Model | Parameters | ONNX Size | Est. Memory | Input Size |
|-------|------------|-----------|-------------|------------|
| EfficientDet-Lite0 | 3.9M | ~15MB | ~30MB | 320×320 |
| RT-DETR-R18 | - | ~80MB | ~160MB | 640×640 |
| RT-DETR-R34 | - | ~160MB | ~320MB | 640×640 |
| RT-DETR-R50 | - | ~200MB | ~400MB | 640×640 |
| RT-DETR-R101 | - | ~300MB | ~600MB | 640×640 |

**권장 선택**:
- 💡 **Fast**: `efficientdet-lite0` (모바일/엣지)
- ⚖️ **Default**: `rt-detr-r34`
- 🚀 **Quality**: `rt-detr-r101`

---

### Segmenter (이미지 분할)

| Model | Parameters | ONNX Size | Est. Memory | Classes |
|-------|------------|-----------|-------------|---------|
| SegFormer-B0 | 3.8M | ~15MB | ~30MB | 150 |
| SegFormer-B1 | 13.7M | ~55MB | ~110MB | 150 |
| SegFormer-B2 | 27.4M | ~110MB | ~220MB | 150 |
| SegFormer-B3 | 47.3M | ~190MB | ~380MB | 150 |
| SegFormer-B4 | 64.1M | ~256MB | ~512MB | 150 |
| SegFormer-B5 | 84.7M | ~340MB | ~680MB | 150 |

**권장 선택**:
- 💡 **Fast**: `segformer-b0`
- ⚖️ **Default**: `segformer-b2`
- 🚀 **Quality**: `segformer-b5`

---

### Synthesizer (음성 합성)

Piper TTS 모델은 경량입니다:

| Voice | Quality | Size | Est. Memory |
|-------|---------|------|-------------|
| en_US-ryan-medium | Medium | ~20MB | ~40MB |
| en_US-lessac-medium | Medium | ~20MB | ~40MB |
| en_US-amy-low | Low | ~16MB | ~32MB |
| en_US-lessac-high | High | ~64MB | ~128MB |

**모든 Piper 음성은 경량이며 메모리 제약이 거의 없습니다.**

---

### Translator (번역)

| Model | Size | Est. Memory | Direction |
|-------|------|-------------|-----------|
| opus-mt-en-ko | ~300MB | ~600MB | EN→KO |
| opus-mt-ko-en | ~300MB | ~600MB | KO→EN |
| opus-mt-en-zh | ~300MB | ~600MB | EN→ZH |
| opus-mt-zh-en | ~300MB | ~600MB | ZH→EN |

---

### OCR (광학 문자 인식)

OCR은 두 개의 모델을 조합합니다:

| Component | Model | Est. Memory |
|-----------|-------|-------------|
| Detection | DBNet | ~100MB |
| Recognition | CRNN | ~50MB |
| **Total** | - | **~150MB** |

---

### Captioner (이미지 캡셔닝)

| Model | Architecture | Est. Memory |
|-------|--------------|-------------|
| ViT-GPT2 | Encoder-Decoder | ~500MB-1GB |

---

## GPU VRAM vs System RAM

### GPU 사용 시 (CUDA/DirectML/CoreML)

| VRAM | Recommended Models |
|------|-------------------|
| 4GB | Embedder (small), Reranker (mini), Detector (lite) |
| 6GB | Embedder (base), Transcriber (small), Segmenter (B2) |
| 8GB | Embedder (large), Generator (1-3B), Transcriber (medium) |
| 12GB | Generator (3-4B), Transcriber (large-turbo) |
| 16GB+ | Generator (7B+), 동시 다중 모델 |

### CPU 사용 시 (System RAM)

CPU 추론 시 System RAM을 사용합니다:

| RAM | Recommended Usage |
|-----|-------------------|
| 8GB | 소형 모델 1개 (Embedder/Reranker) |
| 16GB | 중형 모델 1개 또는 소형 모델 여러 개 |
| 32GB | 대형 모델 또는 다중 모델 동시 사용 |
| 64GB+ | Generator 대형 모델 + 다른 도메인 조합 |

---

## OOM Prevention Strategies

### 1. 올바른 모델 선택

```csharp
// 하드웨어에 맞는 자동 선택 사용
var embedder = await LocalEmbedder.LoadAsync("auto");
var generator = await LocalGenerator.LoadAsync("auto");
```

`"auto"` 별칭은 `HardwareProfile`을 기반으로 최적 모델을 자동 선택합니다.

### 2. Lazy Loading 활용

모델은 `LoadAsync` 호출 시 로딩됩니다. 필요할 때만 로드하세요:

```csharp
// ❌ 모든 모델을 미리 로드
var embedder = await LocalEmbedder.LoadAsync("default");
var reranker = await LocalReranker.LoadAsync("default");
var generator = await LocalGenerator.LoadAsync("default"); // OOM 위험!

// ✅ 필요할 때 로드
await using var embedder = await LocalEmbedder.LoadAsync("default");
// ... embedder 사용 후 자동 해제

await using var reranker = await LocalReranker.LoadAsync("default");
// ... reranker 사용 후 자동 해제
```

### 3. 명시적 해제

`DisposeAsync`로 메모리를 명시적으로 해제합니다:

```csharp
await using (var model = await LocalEmbedder.LoadAsync("large"))
{
    var embeddings = await model.EmbedAsync(texts);
} // 여기서 자동 해제

// 또는 수동 해제
var model = await LocalEmbedder.LoadAsync("large");
try
{
    // 사용
}
finally
{
    await model.DisposeAsync();
}
```

### 4. 배치 크기 조절

큰 입력은 작은 배치로 분할하세요:

```csharp
// ❌ 대량 데이터 한번에 처리
var embeddings = await embedder.EmbedAsync(thousandDocuments);

// ✅ 배치 분할
var results = new List<float[]>();
foreach (var batch in thousandDocuments.Chunk(32))
{
    var batchEmbeddings = await embedder.EmbedAsync(batch);
    results.AddRange(batchEmbeddings);
}
```

### 5. Generator 특수 고려사항

Generator는 메모리 집약적입니다:

```csharp
// GGUF 양자화 모델 사용
var generator = await LocalGenerator.LoadAsync("model-q4_k_m.gguf");

// 또는 작은 ONNX 모델
var generator = await LocalGenerator.LoadAsync("microsoft/Phi-4-mini-instruct-onnx");
```

### 6. Memory 확인

`EstimatedMemoryBytes`로 예상 메모리를 확인합니다:

```csharp
var model = await LocalEmbedder.LoadAsync("large");
var memoryMB = model.EstimatedMemoryBytes / (1024 * 1024);
Console.WriteLine($"Estimated memory: {memoryMB}MB");
```

---

## Performance Tiers Reference

LMSupply는 `HardwareProfile.Current.Tier`를 기반으로 자동 모델 선택합니다:

| Tier | GPU VRAM | System RAM | Recommended |
|------|----------|------------|-------------|
| Low | < 4GB or CPU only | < 16GB | 경량 모델만 |
| Medium | 4-8GB | 16GB+ | 기본 모델 |
| High | 8-16GB | 32GB+ | 대형 모델 |
| Ultra | 16GB+ | 64GB+ | 최대 품질 |

```csharp
var profile = HardwareProfile.Current;
Console.WriteLine($"Tier: {profile.Tier}");
Console.WriteLine($"GPU: {profile.GpuInfo?.Name ?? "None"}");
Console.WriteLine($"VRAM: {profile.GpuInfo?.VramBytes / (1024*1024*1024)}GB");
Console.WriteLine($"Recommended Provider: {profile.RecommendedProvider}");
```

---

## Troubleshooting

### Out of Memory Error

1. **더 작은 모델 사용**: `"default"` → `"fast"`
2. **Provider 변경**: `ExecutionProvider.Cpu` (System RAM 사용)
3. **다른 모델 해제**: `await otherModel.DisposeAsync()`
4. **배치 크기 감소**: 한 번에 처리하는 데이터량 줄이기

### CUDA Out of Memory

```csharp
// DirectML로 대체 (Windows)
var options = new EmbedderOptions { Provider = ExecutionProvider.DirectML };

// 또는 CPU 사용
var options = new EmbedderOptions { Provider = ExecutionProvider.Cpu };
```

### Memory Leak 의심 시

모델을 반드시 `DisposeAsync`로 해제하세요:

```csharp
// ✅ using 문 사용 권장
await using var model = await LocalEmbedder.LoadAsync("default");
```

---

## Related Documentation

- [GPU Providers Guide](GPU_PROVIDERS.md) - GPU 프로바이더 선택
- [Model Lifecycle Guide](MODEL_LIFECYCLE.md) - 모델 생명주기
- [Troubleshooting Guide](TROUBLESHOOTING.md) - 문제 해결
