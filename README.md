# LMSupply

**Local Model Supply for .NET — on-demand AI inference**

[![CI](https://github.com/iyulab/lm-supply/actions/workflows/ci.yml/badge.svg)](https://github.com/iyulab/lm-supply/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

<p align="center">
  <img src="images/1.png" width="49%" alt="LMSupply Console"/>
  <img src="images/2.png" width="49%" alt="LMSupply Console"/>
</p>
<p align="center">
  <img src="images/3.png" width="49%" alt="LMSupply Console"/>
  <img src="images/4.png" width="49%" alt="LMSupply Console"/>
</p>

> Start small. Download what you need. Run locally.

```csharp
// This is all you need. No setup. No configuration. No API keys.
await using var model = await LocalEmbedder.LoadAsync("auto");  // Hardware-optimized selection
float[] embedding = await model.EmbedAsync("Hello, world!");
```

LMSupply is designed around three core principles:

### 🪶 Minimal Footprint
Your application ships with **zero bundled models**. The base package is tiny. Models, tokenizers, and runtime components are downloaded **only when first requested** and cached for reuse.

### ⚡ Lazy Everything
```
First run:  LoadAsync("default") → Downloads model → Caches → Runs inference
Next runs:  LoadAsync("default") → Uses cached model → Runs inference instantly
```
No pre-download scripts. No model management. Just use it.

### 🎯 Zero Boilerplate
Traditional approach:
```csharp
// ❌ Without LMSupply: 50+ lines of setup
var tokenizer = LoadTokenizer(modelPath);
var session = new InferenceSession(modelPath, sessionOptions);
var inputIds = tokenizer.Encode(text);
var attentionMask = CreateAttentionMask(inputIds);
var inputs = new List<NamedOnnxValue> { ... };
var outputs = session.Run(inputs);
var embeddings = PostProcess(outputs);
// ... error handling, pooling, normalization, cleanup ...
```

```csharp
// ✅ With LMSupply: 2 lines
await using var model = await LocalEmbedder.LoadAsync("default");
float[] embedding = await model.EmbedAsync("Hello, world!");
```

---

## Packages

| Package | Description | Status |
|---------|-------------|--------|
| [LMSupply.Embedder](docs/embedder.md) | Text → Vector embeddings (ONNX + GGUF) | [![NuGet](https://img.shields.io/nuget/v/LMSupply.Embedder.svg)](https://www.nuget.org/packages/LMSupply.Embedder) |
| [LMSupply.Reranker](docs/reranker.md) | Semantic reranking for search | [![NuGet](https://img.shields.io/nuget/v/LMSupply.Reranker.svg)](https://www.nuget.org/packages/LMSupply.Reranker) |
| [LMSupply.Generator](docs/generator.md) | Text generation & chat (ONNX + GGUF) | [![NuGet](https://img.shields.io/nuget/v/LMSupply.Generator.svg)](https://www.nuget.org/packages/LMSupply.Generator) |
| [LMSupply.Captioner](docs/captioner.md) | Image → Text captioning | [![NuGet](https://img.shields.io/nuget/v/LMSupply.Captioner.svg)](https://www.nuget.org/packages/LMSupply.Captioner) |
| [LMSupply.Ocr](docs/ocr.md) | Document OCR | [![NuGet](https://img.shields.io/nuget/v/LMSupply.Ocr.svg)](https://www.nuget.org/packages/LMSupply.Ocr) |
| [LMSupply.Detector](docs/detector.md) | Object detection | [![NuGet](https://img.shields.io/nuget/v/LMSupply.Detector.svg)](https://www.nuget.org/packages/LMSupply.Detector) |
| [LMSupply.Segmenter](docs/segmenter.md) | Image segmentation | [![NuGet](https://img.shields.io/nuget/v/LMSupply.Segmenter.svg)](https://www.nuget.org/packages/LMSupply.Segmenter) |
| [LMSupply.Translator](docs/translator.md) | Neural machine translation | [![NuGet](https://img.shields.io/nuget/v/LMSupply.Translator.svg)](https://www.nuget.org/packages/LMSupply.Translator) |
| [LMSupply.Transcriber](docs/transcriber.md) | Speech → Text (Whisper) | [![NuGet](https://img.shields.io/nuget/v/LMSupply.Transcriber.svg)](https://www.nuget.org/packages/LMSupply.Transcriber) |
| [LMSupply.Synthesizer](docs/synthesizer.md) | Text → Speech (Piper) | [![NuGet](https://img.shields.io/nuget/v/LMSupply.Synthesizer.svg)](https://www.nuget.org/packages/LMSupply.Synthesizer) |
| [LMSupply.Llama](docs/llama.md) | Shared llama-server management for GGUF | [![NuGet](https://img.shields.io/nuget/v/LMSupply.Llama.svg)](https://www.nuget.org/packages/LMSupply.Llama) |

---

## Quick Start

### Text Embeddings

```csharp
using LMSupply.Embedder;

// Use "auto" for hardware-optimized model selection
await using var model = await LocalEmbedder.LoadAsync("auto");

// Single text
float[] embedding = await model.EmbedAsync("Hello, world!");

// Batch processing
float[][] embeddings = await model.EmbedAsync(new[]
{
    "First document",
    "Second document",
    "Third document"
});

// Similarity
float similarity = LocalEmbedder.CosineSimilarity(embeddings[0], embeddings[1]);

// GGUF models (via llama-server) - Auto-detected by repo name pattern
await using var ggufModel = await LocalEmbedder.LoadAsync("nomic-ai/nomic-embed-text-v1.5-GGUF");
float[] ggufEmbedding = await ggufModel.EmbedAsync("Hello from GGUF!");
```

### Semantic Reranking

```csharp
using LMSupply.Reranker;

await using var reranker = await LocalReranker.LoadAsync("default");

var results = await reranker.RerankAsync(
    query: "What is machine learning?",
    documents: new[]
    {
        "Machine learning is a subset of artificial intelligence...",
        "The weather today is sunny and warm...",
        "Deep learning uses neural networks..."
    },
    topK: 2
);

foreach (var result in results)
{
    Console.WriteLine($"[{result.Score:F4}] {result.Document}");
}
```

### Text Generation

```csharp
using LMSupply.Generator;

// GGUF models (default) — native tool calling support via llama-server
await using var model = await LocalGenerator.LoadAsync("gguf:default");  // Hermes 3 8B

await foreach (var token in model.GenerateAsync("Hello, my name is"))
{
    Console.Write(token);
}

// Chat with tool calling support (--jinja enabled)
var messages = new[]
{
    ChatMessage.System("You are a helpful assistant."),
    ChatMessage.User("Explain quantum computing simply.")
};

await foreach (var token in model.GenerateChatAsync(messages))
{
    Console.Write(token);
}

// ONNX models (for DirectML/NPU environments)
var generator = await TextGeneratorBuilder.Create()
    .WithDefaultModel()  // Platform-aware: Gemma 4 GGUF on NVIDIA/CPU/Mac/Linux, Phi-4 Mini ONNX on DirectML+non-NVIDIA
    .BuildAsync();

string response = await generator.GenerateCompleteAsync("What is machine learning?");
```

### Translation

```csharp
using LMSupply.Translator;

await using var translator = await LocalTranslator.LoadAsync("ko-en");

// Translate Korean to English
string english = await translator.TranslateAsync("안녕하세요, 세계!");
Console.WriteLine(english); // "Hello, world!"

// Batch translation
string[] translations = await translator.TranslateBatchAsync(new[]
{
    "첫 번째 문장입니다.",
    "두 번째 문장입니다."
});
```

### Speech Recognition (Transcriber)

```csharp
using LMSupply.Transcriber;

await using var transcriber = await LocalTranscriber.LoadAsync("default");

// Transcribe audio file
var result = await transcriber.TranscribeAsync("audio.wav");
Console.WriteLine(result.Text);
Console.WriteLine($"Language: {result.Language}");

// Streaming transcription
await foreach (var segment in transcriber.TranscribeStreamingAsync("audio.wav"))
{
    Console.WriteLine($"[{segment.Start:F2}s] {segment.Text}");
}
```

### Text-to-Speech (Synthesizer)

```csharp
using LMSupply.Synthesizer;

await using var synthesizer = await LocalSynthesizer.LoadAsync("default");

// Synthesize and save to file
await synthesizer.SynthesizeToFileAsync("Hello, world!", "output.wav");

// Get audio samples
var result = await synthesizer.SynthesizeAsync("Hello!");
Console.WriteLine($"Duration: {result.DurationSeconds:F2}s");
Console.WriteLine($"Real-time factor: {result.RealTimeFactor:F1}x");
```

---

## Available Models

*Updated: 2026-03 based on MTEB leaderboard and community benchmarks*

### Embedder (ONNX)

| Alias | Model | Dims | Params | Context | Best For |
|-------|-------|------|--------|---------|----------|
| `default` | bge-small-en-v1.5 | 384 | 33M | 512 | Balanced speed/quality |
| `fast` | all-MiniLM-L6-v2 | 384 | 22M | 256 | Ultra-low latency |
| `quality` | bge-base-en-v1.5 | 768 | 110M | 512 | Higher accuracy |
| `large` | nomic-embed-text-v1.5 | 768 | 137M | 8192 | Long context RAG |
| `multilingual` | bge-m3 | 1024 | 568M | 8192 | 100+ languages, SOTA |

### Embedder (GGUF via llama-server)

GGUF models are auto-detected by `-GGUF` or `_gguf` in repo name, or `.gguf` file extension.

| Model Repository | Dims | Context | Best For |
|------------------|------|---------|----------|
| `nomic-ai/nomic-embed-text-v1.5-GGUF` | 768 | 8K | Long context, matryoshka |
| `BAAI/bge-small-en-v1.5-GGUF` | 384 | 512 | Compact and fast |
| `BAAI/bge-base-en-v1.5-GGUF` | 768 | 512 | Quality balance |
| Any HuggingFace GGUF embedding repo | varies | varies | Custom models |

### Reranker (ONNX)

| Alias | Model | Params | Context | Best For |
|-------|-------|--------|---------|----------|
| `default` | ms-marco-MiniLM-L-6-v2 | 22M | 512 | Balanced speed/quality |
| `fast` | ms-marco-TinyBERT-L-2-v2 | 4.4M | 512 | Ultra-low latency |
| `quality` | bge-reranker-base | 278M | 512 | Higher accuracy |
| `large` | bge-reranker-large | 560M | 512 | Best accuracy |
| `multilingual` | bge-reranker-v2-m3 | 568M | 8192 | Long docs, 100+ languages |

### Reranker (GGUF via llama-server)

GGUF reranker models are auto-detected by `-GGUF` or `_gguf` in repo name.

| Model Repository | Context | Best For |
|------------------|---------|----------|
| `BAAI/bge-reranker-v2-m3-GGUF` | 8K | Multilingual, long docs |
| `jinaai/jina-reranker-v2-base-multilingual-GGUF` | 8K | Multilingual |

### Generator

**Platform-based defaults** (`default` and `auto` delegate to this matrix):

| Platform | Selected backend | Selected model |
|----------|------------------|----------------|
| Windows + NVIDIA | GGUF (llama.cpp CUDA) | Gemma 4 via `gguf:auto` (VRAM-aware) |
| Windows + AMD/Intel GPU | ONNX (DirectML) | Phi-4 Mini (MIT, FC-capable) |
| Windows / Linux CPU-only | GGUF (llama.cpp CPU) | Gemma 4 via `gguf:auto` (VRAM-aware) |
| Linux + any GPU | GGUF (llama.cpp; CUDA on NVIDIA, CPU/ROCm on AMD) | Gemma 4 via `gguf:auto` |
| macOS (Apple Silicon) | GGUF (llama.cpp Metal) | Gemma 4 via `gguf:auto` |

> `LoadAsync("default")` and `LoadAsync("auto")` both route through this matrix. For explicit selection, use `gguf:*` aliases, ONNX aliases, or a direct HuggingFace repo ID.

**ONNX aliases** (recommended for Windows DirectML + non-NVIDIA):

| Alias | Model | Params | Context | License | Notes |
|-------|-------|--------|---------|---------|-------|
| `phi-4-mini` | Phi-4-mini-instruct | 3.8B | 16K | MIT | Smallest FC-capable ONNX model |
| `fast` | Phi-4-mini-instruct | 3.8B | 16K | MIT | Same as `phi-4-mini` |
| `quality` | phi-4 | 14B | 16K | MIT | Best reasoning |
| `phi-3.5-mini` | Phi-3.5-mini-instruct | 3.8B | 128K | MIT | Long context (legacy) |

**GGUF aliases** (via llama-server):

Gemma 4 중심 레지스트리 (Apache 2.0, 멀티모달, 네이티브 function calling). llama.cpp **b8672+** 필요 — `gguf:fast`/`default`/`balanced`/`quality`/`large` 로딩 시 최소 버전이 자동 검증됩니다.

| Alias | Model | Params | Quant | Size | VRAM Target |
|-------|-------|--------|-------|------|-------------|
| `gguf:auto` | Hardware-optimized | varies | varies | varies | Auto-select |
| `gguf:fast` | Gemma 4 E2B Instruct | 2.3B | Q4_K_M | ~3.1 GB | <4GB iGPU/mobile |
| `gguf:default` | Gemma 4 E4B Instruct | 4.5B | Q4_K_M | ~5.3 GB | 4-8GB |
| `gguf:balanced` | Gemma 4 E4B Instruct | 4.5B | Q8_0 | ~7.5 GB | 8-16GB (RTX 3060 12GB 등) |
| `gguf:quality` | Gemma 4 26B A4B (MoE) | 26B (4B active) | Q4_K_M | ~16.8 GB | 16-20GB |
| `gguf:large` | Gemma 4 31B Instruct | 31B | Q4_K_M | ~18.7 GB | 20-48GB |
| `gguf:xlarge` | Qwen 3.5 122B A10B (MoE, split) | 122B (10B active) | Q4_K_M | ~76.5 GB (3 shards) | 48GB+ server |

### Translator

| Alias | Direction | Model | Best For |
|-------|-----------|-------|----------|
| `ko-en` | Korean → English | OPUS-MT | Korean translation |
| `en-ko` | English → Korean | OPUS-MT | Korean translation |
| `ja-en` | Japanese → English | OPUS-MT | Japanese translation |
| `zh-en` | Chinese → English | OPUS-MT | Chinese translation |
| `multilingual` | Many → English | mBART/M2M100 | 100+ languages |

### Transcriber (Whisper)

| Alias | Model | Params | Size | WER | Best For |
|-------|-------|--------|------|-----|----------|
| `fast` | Whisper Tiny | 39M | ~150MB | 7.6% | Ultra-fast transcription |
| `default` | Whisper Base | 74M | ~290MB | 5.0% | Balanced speed/quality |
| `quality` | Whisper Small | 244M | ~970MB | 3.4% | Higher accuracy |
| `large` | Whisper Large V3 | 1.5B | ~6GB | 2.5% | Best accuracy |
| `english` | Whisper Base.en | 74M | ~290MB | 4.3% | English-optimized |

### Synthesizer (Piper TTS)

| Alias | Voice | Language | Sample Rate | Best For |
|-------|-------|----------|-------------|----------|
| `default` | Lessac | en-US | 22050 Hz | Balanced quality |
| `fast` | Ryan | en-US | 16000 Hz | Ultra-fast synthesis |
| `quality` | Amy | en-US | 22050 Hz | High quality |
| `british` | Semaine | en-GB | 22050 Hz | British English |
| `korean` | KSS | ko-KR | 22050 Hz | Korean |
| `japanese` | JSUT | ja-JP | 22050 Hz | Japanese |
| `chinese` | Huayan | zh-CN | 22050 Hz | Mandarin Chinese |

---

## Adaptive Model Selection ("auto" mode)

Use `"auto"` to let LMSupply select the optimal model based on your hardware:

```csharp
// Hardware-optimized model selection
await using var embedder = await LocalEmbedder.LoadAsync("auto");
await using var generator = await LocalGenerator.LoadAsync("auto");      // Platform-based: GGUF or ONNX
await using var reranker = await LocalReranker.LoadAsync("auto");
```

LMSupply detects your hardware and selects models accordingly:

### ONNX Models

| Performance Tier | Hardware | Embedder | Generator | Reranker |
|------------------|----------|----------|-----------|----------|
| **Low** | CPU only or GPU <4GB | bge-small (33M) | Phi-4-mini (3.8B) | MiniLM-L6 (22M) |
| **Medium** | GPU 4-8GB | bge-base (110M) | Phi-4-mini (3.8B) | bge-reranker-base |
| **High** | GPU 8-16GB | gte-large (434M) | Phi-4 (14B) | bge-reranker-large |
| **Ultra** | GPU 16GB+ | gte-large (434M) | Phi-4 (14B) | bge-reranker-large |

### GGUF Models (via `gguf:auto`)

| Performance Tier | Hardware | GGUF Generator |
|------------------|----------|----------------|
| **Low** | CPU only or GPU <4GB | Ministral 3 3B |
| **Medium** | GPU 4-8GB | Hermes 3 8B |
| **High** | GPU 8-16GB | Mistral Nemo 12B |
| **Ultra** | GPU 16GB+ | Qwen 3 32B |

> **Platform-based routing (v0.28.0+):** `LoadAsync("default")` and `LoadAsync("auto")` both select the optimal backend+model for the current host: GGUF via llama.cpp on CPU / NVIDIA / Apple Silicon / Linux, and ONNX via DirectML on Windows AMD/Intel. Use `gguf:*` aliases or ONNX aliases for explicit control.

**Key benefits:**
- **Zero configuration** - Just use `"auto"`, no hardware research needed
- **Optimal performance** - Larger models on capable hardware
- **Graceful degradation** - Smaller models on limited hardware
- **Backward compatible** - Existing aliases (`"default"`, `"fast"`, `"quality"`) still work

---

## GPU Acceleration

GPU acceleration is **automatic** — LMSupply detects your hardware and downloads appropriate runtime binaries on first use:

```
Detection priority: CUDA → DirectML → CoreML → CPU
```

```csharp
// Auto-detect (default) - uses GPU if available, falls back to CPU
var options = new EmbedderOptions { Provider = ExecutionProvider.Auto };

// Force specific provider
var options = new EmbedderOptions { Provider = ExecutionProvider.Cuda };     // NVIDIA
var options = new EmbedderOptions { Provider = ExecutionProvider.DirectML }; // Windows GPU
var options = new EmbedderOptions { Provider = ExecutionProvider.CoreML };   // macOS
```

### Verify GPU Detection

```csharp
using LMSupply.Runtime;

// Quick summary (returns formatted string)
Console.WriteLine(EnvironmentDetector.GetEnvironmentSummary());

// Or access individual properties
var gpu = EnvironmentDetector.DetectGpu();
var provider = EnvironmentDetector.GetRecommendedProvider();

Console.WriteLine($"Provider: {provider}");
Console.WriteLine($"CUDA Available: {gpu.Vendor == GpuVendor.Nvidia && gpu.CudaDriverVersionMajor >= 11}");
Console.WriteLine($"DirectML Available: {gpu.DirectMLSupported}");
```

### Troubleshooting GPU Issues

**Do NOT install ONNX Runtime packages manually.** LMSupply handles runtime binary management automatically via lazy downloading.

If you have conflicting packages installed, remove them:

```bash
dotnet remove package Microsoft.ML.OnnxRuntime
dotnet remove package Microsoft.ML.OnnxRuntime.Gpu
dotnet remove package Microsoft.ML.OnnxRuntime.DirectML
```

For NVIDIA CUDA support, ensure you have:
- NVIDIA GPU drivers installed
- CUDA 11.x or 12.x runtime (LMSupply auto-selects the appropriate version)

---

## Thread Safety & Batch Processing

All LMSupply models are **thread-safe** for concurrent inference. ONNX Runtime's `InferenceSession.Run()` is thread-safe by design.

```csharp
// Safe: Concurrent inference on the same model instance
await using var embedder = await LocalEmbedder.LoadAsync("default");

await Parallel.ForEachAsync(documents, async (doc, ct) =>
{
    var embedding = await embedder.EmbedAsync(doc, ct);
    // Process embedding...
});

// Or with Task.WhenAll
var tasks = documents.Select(d => embedder.EmbedAsync(d));
var embeddings = await Task.WhenAll(tasks);
```

**Performance tips:**
- GPU inference: 2-4 concurrent operations typically optimal
- CPU inference: Match `MaxDegreeOfParallelism` to core count
- Use `EmbedBatchAsync()` when available for better throughput

---

## Loading Models

LMSupply supports three ways to specify models:

### 1. Aliases (Recommended for beginners)

Use predefined aliases for quick access to popular models:

```csharp
await using var embedder = await LocalEmbedder.LoadAsync("default");      // bge-small-en-v1.5
await using var embedder = await LocalEmbedder.LoadAsync("multilingual"); // bge-m3
await using var generator = await LocalGenerator.LoadAsync("gguf:auto");    // Hardware-optimized
await using var generator = await LocalGenerator.LoadAsync("gguf:quality"); // Mistral Nemo 12B
```

### 2. HuggingFace Repository ID (Full control)

Use any HuggingFace repository directly with `owner/repo-name` format:

```csharp
// ONNX models - auto-discovers onnx/ subfolder
await using var embedder = await LocalEmbedder.LoadAsync("BAAI/bge-large-en-v1.5");
await using var reranker = await LocalReranker.LoadAsync("BAAI/bge-reranker-v2-m3");

// GGUF models - auto-detected by repo name pattern (-GGUF, _gguf)
await using var generator = await LocalGenerator.LoadAsync("bartowski/Llama-3.2-3B-Instruct-GGUF");
await using var generator = await LocalGenerator.LoadAsync("bartowski/Qwen2.5-Coder-7B-Instruct-GGUF");

// Vision models
await using var captioner = await LocalCaptioner.LoadAsync("microsoft/Florence-2-base");
await using var detector = await LocalDetector.LoadAsync("onnx-community/yolov8s");
```

The system automatically:
- Discovers ONNX files via HuggingFace API
- Detects subfolder structure (`onnx/`, `cpu/`, `cuda/`)
- Selects appropriate quantization variants (Q4_K_M for GGUF)
- Downloads required tokenizer and config files

### 3. Local Path

Use locally stored models:

```csharp
// ONNX model directory
await using var embedder = await LocalEmbedder.LoadAsync("/path/to/model-directory");

// GGUF file directly
await using var generator = await LocalGenerator.LoadAsync("/path/to/model.gguf");
```

For private HuggingFace repositories, set the `HF_TOKEN` environment variable.

---

## Model Caching

Models are cached following HuggingFace Hub conventions:

- **Default**: `~/.cache/huggingface/hub`
- **Environment variables**: `HF_HUB_CACHE`, `HF_HOME`, or `XDG_CACHE_HOME`
- **Manual override**: `new EmbedderOptions { CacheDirectory = "/path/to/cache" }`

---

## Requirements

### Software
- .NET 10.0+
- Windows 10+, Linux, or macOS 11+

### Hardware (Recommended)

| Use Case | RAM | GPU VRAM | Notes |
|----------|-----|----------|-------|
| **Embeddings** | 4GB+ | Optional | CPU works fine for small models |
| **Reranking** | 8GB+ | 4GB+ | GPU recommended for large models |
| **Text Generation** | 16GB+ | 8GB+ | VRAM strongly recommended |
| **Speech (Whisper)** | 8GB+ | 4GB+ | GPU significantly faster |
| **Vision (Detection/Captioning)** | 8GB+ | 4GB+ | GPU recommended |

**Minimum for "auto" mode:**
- Any modern CPU with 8GB RAM
- For best experience: NVIDIA GPU with 8GB+ VRAM

---

## Documentation

### Getting Started
- [Model Lifecycle](docs/MODEL_LIFECYCLE.md) - Loading, using, and disposing models
- [GPU Providers](docs/GPU_PROVIDERS.md) - GPU acceleration and provider selection
- [Memory Requirements](docs/MEMORY_REQUIREMENTS.md) - Model memory requirements and OOM prevention
- [Troubleshooting](docs/TROUBLESHOOTING.md) - Common issues and solutions

### Text & Language
- [Embedder Guide](docs/embedder.md) - Text → Vector embeddings
- [Reranker Guide](docs/reranker.md) - Semantic reranking
- [Generator Guide](docs/generator.md) - Text generation & chat
- [Translator Guide](docs/translator.md) - Neural machine translation

### Vision
- [Captioner Guide](docs/captioner.md) - Image → Text captioning
- [OCR Guide](docs/ocr.md) - Document text recognition
- [Detector Guide](docs/detector.md) - Object detection
- [Segmenter Guide](docs/segmenter.md) - Image segmentation

### Audio
- [Transcriber Guide](docs/transcriber.md) - Speech → Text (Whisper)
- [Synthesizer Guide](docs/synthesizer.md) - Text → Speech (Piper)

---

## License

MIT License - see [LICENSE](LICENSE) for details.