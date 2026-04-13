# LMSupply.Generator

Local text generation and chat with ONNX Runtime GenAI and GGUF (llama-server) support.

## Installation

```bash
dotnet add package LMSupply.Generator
```

## Quick Start

### Simple Text Generation

```csharp
using LMSupply.Generator;

// Using the builder pattern
var generator = await TextGeneratorBuilder.Create()
    .WithDefaultModel()        // Platform-aware: Gemma 4 GGUF on NVIDIA/CPU/Mac/Linux, Phi-4 Mini ONNX on DirectML+non-NVIDIA
    .BuildAsync();

// Generate text
string response = await generator.GenerateCompleteAsync("What is machine learning?");
Console.WriteLine(response);

await generator.DisposeAsync();
```

### Chat Completion

```csharp
using LMSupply.Generator;
using LMSupply.Generator.Models;

var generator = await TextGeneratorBuilder.Create()
    .WithDefaultModel()  // see "Default and auto selection" below
    .BuildAsync();

// Chat format
var messages = new[]
{
    new ChatMessage(ChatRole.System, "You are a helpful assistant."),
    new ChatMessage(ChatRole.User, "Explain quantum computing in simple terms.")
};

string response = await generator.GenerateChatCompleteAsync(messages);
Console.WriteLine(response);
```

### Streaming Generation

```csharp
await foreach (var token in generator.GenerateAsync("Write a short story about a robot:"))
{
    Console.Write(token);
}
```

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

## Model Selection

### Preset Models

```csharp
// Default: Phi-4 Mini (balanced, MIT license)
.WithDefaultModel()

// Or use presets
.WithModel(GeneratorModelPreset.Default)   // Phi-4 Mini
.WithModel(GeneratorModelPreset.Fast)      // Phi-4 Mini (smallest FC-capable)
.WithModel(GeneratorModelPreset.Quality)   // Phi-4
```

### HuggingFace Models

```csharp
// Use any ONNX model from HuggingFace
.WithHuggingFaceModel("microsoft/Phi-4-mini-instruct-onnx")
```

### Local Models

```csharp
// Use a local model directory
.WithModelPath("C:/models/my-model-onnx")
```

## Configuration Options

### Execution Provider

```csharp
var generator = await TextGeneratorBuilder.Create()
    .WithDefaultModel()
    .WithProvider(ExecutionProvider.Auto)      // Auto-detect best provider
    .WithProvider(ExecutionProvider.Cuda)      // NVIDIA GPU
    .WithProvider(ExecutionProvider.DirectML)  // Windows GPU (AMD, Intel, NVIDIA)
    .WithProvider(ExecutionProvider.CoreML)    // macOS Apple Silicon
    .WithProvider(ExecutionProvider.Cpu)       // CPU only
    .BuildAsync();
```

### Generation Options

```csharp
var options = new GeneratorOptions
{
    MaxTokens = 512,              // Maximum tokens to generate
    Temperature = 0.7f,           // Randomness (0.0 = deterministic)
    TopP = 0.9f,                  // Nucleus sampling
    TopK = 50,                    // Top-K sampling
    RepetitionPenalty = 1.1f,     // Discourage repetition
    DoSample = true               // Enable sampling (vs greedy)
};

string response = await generator.GenerateCompleteAsync(prompt, options);

// Or use presets
string creative = await generator.GenerateCompleteAsync(prompt, GeneratorOptions.Creative);
string precise = await generator.GenerateCompleteAsync(prompt, GeneratorOptions.Precise);
```

### Memory Management

```csharp
// Limit memory usage
var generator = await TextGeneratorBuilder.Create()
    .WithDefaultModel()
    .WithMemoryLimit(8.0)    // 8GB limit
    .BuildAsync();

// Or with detailed options
var memoryOptions = new MemoryAwareOptions
{
    MaxMemoryBytes = 8L * 1024 * 1024 * 1024,  // 8GB
    WarningThreshold = 0.80,                    // GC at 80%
    CriticalThreshold = 0.95,                   // Fail at 95%
    AutoGcOnWarning = true
};

var generator = await TextGeneratorBuilder.Create()
    .WithDefaultModel()
    .WithMemoryManagement(memoryOptions)
    .BuildAsync();
```

## Hardware Detection

```csharp
using LMSupply.Generator;

// Get hardware recommendations
var recommendation = HardwareDetector.GetRecommendation();

Console.WriteLine(recommendation.GetSummary());
// Output:
// Hardware: NVIDIA RTX 4090 (24.0GB)
// System Memory: 64.0GB
// Provider: Cuda
// Quantization: FP16
// Max Context: 16384
// Recommended Models: microsoft/Phi-4-mini-instruct-onnx, microsoft/phi-4-onnx

// Auto-select best provider
var provider = HardwareDetector.GetBestProvider();
```

## Speculative Decoding

Speed up generation by using a smaller draft model:

```csharp
using LMSupply.Generator;

// Create draft (small/fast) and target (large/accurate) models
var draftModel = await TextGeneratorBuilder.Create()
    .WithModel(GeneratorModelPreset.Fast)
    .BuildAsync();

var targetModel = await TextGeneratorBuilder.Create()
    .WithModel(GeneratorModelPreset.Quality)
    .BuildAsync();

// Create speculative decoder
var decoder = SpeculativeDecoderBuilder.Create()
    .WithDraftModel(draftModel)
    .WithTargetModel(targetModel)
    .WithSpeculationLength(5)
    .WithAdaptiveSpeculation(true)
    .Build();

// Generate with speculative decoding
var result = await decoder.GenerateCompleteAsync("Explain neural networks:");

Console.WriteLine(result.Text);
Console.WriteLine(result.Stats.GetSummary());
// Output:
// Total Tokens: 256
// Draft/Accepted: 200/180 (90.0%)
// Target Tokens: 76
// Throughput: 45.2 tok/s
// Time: 5667ms
```

## Model Factory

For advanced scenarios with multiple models:

```csharp
using LMSupply.Generator;

using var factory = new OnnxGeneratorModelFactory();

// Check if model is available locally
if (!factory.IsModelAvailable("microsoft/Phi-4-mini-instruct-onnx"))
{
    // Download model
    await factory.DownloadModelAsync(
        "microsoft/Phi-4-mini-instruct-onnx",
        progress: new Progress<double>(p => Console.WriteLine($"Downloading: {p:P0}"))
    );
}

// Create model instance
var model = await factory.CreateAsync("microsoft/Phi-4-mini-instruct-onnx");

// List available models
foreach (var modelId in factory.GetAvailableModels())
{
    Console.WriteLine(modelId);
}
```

## Well-Known Models

| Alias | Model | Parameters | License |
|-------|-------|------------|---------|
| Default | Phi-4-mini-instruct | 3.8B | MIT |
| Fast | Phi-4-mini-instruct | 3.8B | MIT |
| Quality | phi-4 | 14B | MIT |

## Chat Formats

The library automatically detects chat format based on model ID:

| Format | Models |
|--------|--------|
| Phi-3 | Phi-3, Phi-3.5, Phi-4 (ONNX) |
| ChatML | Hermes 3, Qwen 3 (GGUF) |
| Mistral Nemo | Ministral, Mistral Nemo (GGUF) |

Or specify explicitly:

```csharp
var generator = await TextGeneratorBuilder.Create()
    .WithHuggingFaceModel("my-model")
    .WithChatFormat("phi3")   // phi3, llama3, chatml, gemma
    .BuildAsync();
```

## GPU Support

GPU acceleration is **automatic** — LMSupply detects your hardware and downloads appropriate runtime binaries on first use:

- **NVIDIA CUDA**: Automatically detected and used
- **Windows DirectML**: AMD, Intel, NVIDIA via Direct3D
- **macOS CoreML**: Apple Silicon optimization

No additional packages required. Use `ExecutionProvider.Auto` (default) or force specific provider in options.

## GGUF Model Support

GGUF models are loaded via [llama-server](https://github.com/ggml-org/llama.cpp) (llama.cpp HTTP server), providing access to the vast ecosystem of quantized models on HuggingFace. The llama-server binaries are automatically downloaded and managed.

### Quick Start with GGUF

```csharp
using LMSupply.Generator;

// Load a GGUF model using the "gguf:" prefix
await using var model = await LocalGenerator.LoadAsync("gguf:default");

// Generate text
await foreach (var token in model.GenerateAsync("Hello, my name is"))
{
    Console.Write(token);
}
```

### GGUF Model Aliases

The default GGUF registry centers on **Gemma 4** (Apache 2.0, multimodal-capable, native function calling). Loading any Gemma 4 alias requires **llama.cpp b8672+** — the minimum version is automatically validated at load time.

| Alias | Model | Parameters | Quant | Size | Use Case |
|-------|-------|------------|-------|------|----------|
| `gguf:auto` | Hardware-optimized | varies | varies | varies | Auto-select by VRAM |
| `gguf:fast` | Gemma 4 E2B Instruct | 2.3B | Q4_K_M | ~3.1 GB | <4GB iGPU/mobile |
| `gguf:default` | Gemma 4 E4B Instruct | 4.5B | Q4_K_M | ~5.3 GB | 4-8GB VRAM |
| `gguf:balanced` | Gemma 4 E4B Instruct | 4.5B | Q8_0 | ~7.5 GB | 8-16GB VRAM (higher quality E4B) |
| `gguf:quality` | Gemma 4 26B A4B (MoE) | 26B (4B active) | Q4_K_M | ~16.8 GB | 16-20GB VRAM |
| `gguf:large` | Gemma 4 31B Instruct | 31B (Dense) | Q4_K_M | ~18.7 GB | 20-48GB VRAM |
| `gguf:xlarge` | Qwen 3.5 122B A10B (MoE, split) | 122B (10B active) | Q4_K_M | ~76.5 GB (3 shards) | 48GB+ server |

> **Split GGUF support**: `gguf:xlarge` is distributed as 3 shards (`-00001-of-00003`, etc.) in a `Q4_K_M/` subfolder. The downloader automatically fetches all shards; llama-server auto-loads the remaining parts when given the first shard path.

#### Hardware-Optimized Selection (`gguf:auto`)

`gguf:auto` selects the largest model that fits in available VRAM (after a 15% safety margin):

| Available VRAM | Selected Model |
|----------------|----------------|
| <4GB or CPU | Gemma 4 E2B (Q4_K_M, 3.1 GB) |
| 4-8GB | Gemma 4 E4B (Q4_K_M, 5.3 GB) |
| 8-16GB | **Gemma 4 E4B (Q8_0, 7.5 GB)** ← `gguf:balanced` |
| 16-20GB | Gemma 4 26B MoE (Q4_K_M, 16.8 GB) |
| 20-48GB | Gemma 4 31B Dense (Q4_K_M, 18.7 GB) |
| 48GB+ | Qwen 3.5 122B MoE (76.5 GB) |

```csharp
// Let LMSupply choose the optimal model for your hardware
await using var model = await LocalGenerator.LoadAsync("gguf:auto");
```

### Using HuggingFace GGUF Repositories

Load any GGUF model directly with `owner/repo-name` format:

```csharp
// Load from any GGUF repository (auto-detected by -GGUF suffix)
await using var model = await LocalGenerator.LoadAsync(
    "NousResearch/Hermes-3-Llama-3.1-8B-GGUF");

// Other popular repositories
await using var model = await LocalGenerator.LoadAsync("bartowski/Mistral-Nemo-Instruct-2407-GGUF");
await using var model = await LocalGenerator.LoadAsync("unsloth/Qwen3-32B-GGUF");
await using var model = await LocalGenerator.LoadAsync("mistralai/Ministral-3-3B-Instruct-2512-GGUF");

// Specify a particular quantization file
await using var model = await LocalGenerator.LoadAsync(
    "bartowski/Qwen2.5-7B-Instruct-GGUF",
    new GeneratorOptions { GgufFileName = "Qwen2.5-7B-Instruct-Q5_K_M.gguf" });
```

The system automatically:
- Detects GGUF repositories by `-GGUF` or `_gguf` suffix in repo name
- Selects the optimal quantization file based on available memory (VRAM + RAM), choosing the largest quantization that fits
- Downloads and caches the model for reuse

> **Hardware-aware selection:** LMSupply measures available VRAM and RAM, then picks the highest-quality quantization (e.g., Q8 over Q4) that fits in memory. Use `GeneratorOptions.GgufFileName` to override with a specific file.

### GGUF Configuration Options

```csharp
var options = new GeneratorOptions
{
    // Context length (default: from model metadata)
    MaxContextLength = 4096
};

await using var model = await LocalGenerator.LoadAsync("gguf:default", options);
```

### Advanced GGUF Options (LlamaOptions)

For fine-grained control over llama.cpp behavior:

```csharp
var options = new GeneratorOptions
{
    MaxContextLength = 8192,
    LlamaOptions = new LlamaOptions
    {
        // GPU layer offloading (-1 = all on GPU, 0 = CPU only, N = N layers)
        GpuLayerCount = -1,

        // Batch size for prompt processing (default: 512)
        BatchSize = 1024,

        // Physical batch size for memory control (must be <= BatchSize)
        UBatchSize = 512,

        // Enable Flash Attention for better performance (requires compatible GPU)
        FlashAttention = true,

        // KV cache quantization for memory savings
        TypeK = KvCacheQuantizationType.Q8_0,  // ~50% KV cache memory reduction
        TypeV = KvCacheQuantizationType.Q8_0,

        // Memory mapping for faster model loading
        UseMemoryMap = true,

        // Lock model in memory to prevent swapping
        UseMemoryLock = false,

        // RoPE frequency settings for context extension
        RopeFrequencyBase = null,
        RopeFrequencyScale = null,

        // Multi-GPU: select primary GPU (0-based index)
        MainGpu = 0,

        // CPU thread count (default: auto-detected)
        Threads = null
    }
};

await using var model = await LocalGenerator.LoadAsync("gguf:quality", options);
```

#### KV Cache Quantization

Quantizing the KV (Key-Value) cache significantly reduces memory usage with minimal quality impact:

| Type | Memory Savings | Quality Impact |
|------|----------------|----------------|
| `F16` (default) | 0% | Best |
| `Q8_0` | ~50% | Minimal |
| `Q4_0` | ~75% | Noticeable on long contexts |
| `F32` | -100% (increases) | Identical to F16 |

```csharp
// Example: Large context with aggressive memory optimization
var options = new GeneratorOptions
{
    MaxContextLength = 32768,
    LlamaOptions = new LlamaOptions
    {
        TypeK = KvCacheQuantizationType.Q4_0,
        TypeV = KvCacheQuantizationType.Q4_0,
        BatchSize = 2048,
        UBatchSize = 256
    }
};
```

#### Automatic Hardware Optimization

If `LlamaOptions` is not specified, LMSupply automatically configures optimal settings:

```csharp
// LlamaOptions.GetOptimalForHardware() is called internally
var autoOptions = LlamaOptions.GetOptimalForHardware();
```

| Tier | BatchSize | UBatchSize | FlashAttention | TypeK/TypeV | GpuLayerCount |
|------|-----------|------------|----------------|-------------|---------------|
| Ultra | 4096 | 1024 | true | Q8_0 | -1 (all GPU) |
| High | 2048 | 512 | true | Q8_0 | -1 (all GPU) |
| Medium | 1024 | 512 | false | Q4_0 | -1 (all GPU) |
| Low | 512 | 256 | false | F16 | 0 (CPU only) |

### Performance Tuning Guide

#### Maximum Throughput

For highest tokens/second on capable hardware:

```csharp
var options = new GeneratorOptions
{
    LlamaOptions = new LlamaOptions
    {
        GpuLayerCount = -1,      // All layers on GPU
        BatchSize = 2048,         // Large batch for throughput
        UBatchSize = 512,         // Balanced physical batch
        FlashAttention = true,    // Faster attention computation
        UseMemoryMap = true       // Faster model loading
    }
};
```

#### Minimum Memory Footprint

For systems with limited VRAM:

```csharp
var options = new GeneratorOptions
{
    MaxContextLength = 4096,     // Limit context for memory
    LlamaOptions = new LlamaOptions
    {
        TypeK = KvCacheQuantizationType.Q4_0,  // Aggressive KV quantization
        TypeV = KvCacheQuantizationType.Q4_0,
        BatchSize = 256,          // Smaller batch size
        UBatchSize = 128,
        FlashAttention = false    // May save memory on some GPUs
    }
};
```

#### Long Context Processing

For handling long documents (8K+ tokens):

```csharp
var options = new GeneratorOptions
{
    MaxContextLength = 32768,
    LlamaOptions = new LlamaOptions
    {
        TypeK = KvCacheQuantizationType.Q8_0,  // Balance memory/quality
        TypeV = KvCacheQuantizationType.Q8_0,
        BatchSize = 2048,
        UBatchSize = 512,
        RopeFrequencyBase = 1000000f  // For YaRN-scaled models
    }
};
```

#### CPU-Only Systems

For systems without GPU acceleration:

```csharp
var options = new GeneratorOptions
{
    Provider = ExecutionProvider.Cpu,
    LlamaOptions = new LlamaOptions
    {
        GpuLayerCount = 0,
        Threads = Environment.ProcessorCount,  // Use all CPU cores
        BatchSize = 512,
        UseMemoryMap = true,
        UseMemoryLock = true   // Prevent swapping (requires privileges)
    }
};
```

### Chat Generation with GGUF

```csharp
using LMSupply.Generator;
using LMSupply.Generator.Models;

await using var model = await LocalGenerator.LoadAsync("gguf:default");

var messages = new[]
{
    ChatMessage.System("You are a helpful assistant."),
    ChatMessage.User("What is the capital of France?")
};

await foreach (var token in model.GenerateChatAsync(messages))
{
    Console.Write(token);
}
```

### Multimodal Content (Vision Models)

`ChatMessage` supports multimodal content via `ContentPart` for vision-capable models like Gemma 4 multimodal:

```csharp
using LMSupply.Generator.Models;

// Convenience: text + single image
var msg = ChatMessage.UserWithImage(
    "What is in this image?",
    "data:image/jpeg;base64,/9j/4AAQSkZJRg...");

// Generic: arbitrary content parts
var multiPart = ChatMessage.UserWithContent(new ContentPart[]
{
    new TextContentPart("Compare these two images:"),
    new ImageContentPart { Url = "https://example.com/a.jpg" },
    new ImageContentPart { Url = "https://example.com/b.jpg" },
    new TextContentPart("Which has more cats?")
});

// IsMultimodal flag for inspection
if (msg.IsMultimodal) { /* contains at least one image */ }
```

**Backward compatibility**: `ChatMessage.User("hi")` works unchanged. `ContentParts` is null for text-only messages, and the `Content` field always holds a text fallback so non-vision formatters keep working.

### Tool Calling with GGUF

GGUF models support native tool calling via the `--jinja` flag (enabled by default). Models with llama.cpp native handlers (Hermes, Mistral Nemo) provide the most stable experience.

```csharp
// Tool calling is automatically available with GGUF models
await using var model = await LocalGenerator.LoadAsync("gguf:default");  // Hermes 3 (chatml handler)

// Tool definitions use OpenAI-compatible format via llama-server
```

> **Recommended models for tool calling:** `gguf:default` (Hermes 3), `gguf:fast` (Ministral 3), `gguf:quality` (Mistral Nemo) — these use llama.cpp native chat handlers for stable tool calling.

### Generation Options

```csharp
var genOptions = new GenerationOptions
{
    MaxTokens = 256,          // Maximum tokens to generate
    Temperature = 0.7f,        // Randomness (0.0 = deterministic)
    TopP = 0.9f,              // Nucleus sampling
    TopK = 40                  // Top-K sampling
};

await foreach (var token in model.GenerateAsync(prompt, genOptions))
{
    Console.Write(token);
}
```

### Reasoning Model Support (DeepSeek R1)

For reasoning models like DeepSeek R1 that output `<think>...</think>` tags:

```csharp
await using var model = await LocalGenerator.LoadAsync("unsloth/DeepSeek-R1-Distill-Llama-8B-GGUF");

// Option 1: Filter reasoning tokens (only show final answer)
var options = new GenerationOptions
{
    FilterReasoningTokens = true
};

await foreach (var token in model.GenerateChatAsync(messages, options))
{
    Console.Write(token); // Reasoning content is filtered out
}

// Option 2: Extract reasoning separately
var result = await model.GenerateChatWithReasoningAsync(messages);
Console.WriteLine($"Answer: {result.Response}");
Console.WriteLine($"Reasoning: {result.Reasoning}");
```

Supported reasoning tag formats:
- `<think>...</think>` (DeepSeek R1)
- `<｜begin▁of▁thinking｜>...<｜end▁of▁thinking｜>` (DeepSeek native format)

### Supported Chat Formats

The library auto-detects chat format from model filenames:

| Format | Models | Notes |
|--------|--------|-------|
| Llama 3 | Llama-3, Llama-3.1, Llama-3.2, CodeLlama | |
| ChatML | Qwen, Yi, InternLM, OpenChat | |
| Gemma | Gemma, Gemma-2 | system → user mapping |
| **Gemma 4** | Gemma 4 (E2B/E4B/26B/31B) | **Native system role** (`<start_of_turn>system`) |
| Phi-3 | Phi-3, Phi-3.5, Phi-4 | |
| Mistral | Mistral, Mixtral | |
| EXAONE | EXAONE | |
| DeepSeek | DeepSeek, DeepSeek-R1 | |
| Vicuna | Vicuna | |
| Zephyr | Zephyr | |

The detector distinguishes Gemma 4 (e.g., `gemma-4-E4B-it-...gguf`) from earlier Gemma generations and routes them to `Gemma4ChatFormatter`, which preserves the system role natively instead of mapping it to user.

### Model Information

```csharp
await using var model = await LocalGenerator.LoadAsync("gguf:default");

var info = model.GetModelInfo();

Console.WriteLine($"Model: {info.ModelId}");
Console.WriteLine($"Path: {info.ModelPath}");
Console.WriteLine($"Context: {info.MaxContextLength}");
Console.WriteLine($"Format: {info.ChatFormat}");
Console.WriteLine($"Provider: {info.ExecutionProvider}");  // "llama-server-Vulkan"
```

### GGUF vs ONNX

| Feature | GGUF (llama-server) | ONNX (GenAI) |
|---------|---------------------|--------------|
| Model availability | Extensive | Limited |
| Quantization options | Many (Q2-Q8) | FP16, INT4 |
| Setup complexity | Simple | Simple |
| GPU support | CUDA, Vulkan, Metal, ROCm | CUDA, DirectML, CoreML |
| Server pooling | Yes (reuses servers) | N/A |
| Memory efficiency | Good | Good |
| Inference speed | Fast | Fast |

## Known Issues

### ONNX GenAI Memory Leak Warnings

When using ONNX models, you may see stderr warnings like:

```
OGA Error: 1 instances of struct Generators::Model were leaked.
OGA Error: 1 instances of struct Generators::Tokenizer were leaked.
```

**This is a known upstream issue** in Microsoft's ONNX Runtime GenAI library, particularly affecting the DirectML backend. The warnings indicate internal resource tracking but **do not affect functionality**.

**Relevant upstream issues:**
- [microsoft/onnxruntime-genai#590](https://github.com/microsoft/onnxruntime-genai/issues/590) - Memory leak during back-to-back inferences
- [microsoft/onnxruntime-genai#1677](https://github.com/microsoft/onnxruntime-genai/issues/1677) - Memory Leak on CUDA

**Workarounds:**
1. The warnings can be safely ignored for most use cases
2. For long-running applications, consider periodic process restarts
3. GGUF models (via llama-server) do not exhibit this issue

**Status:** Tracking upstream fixes. LMSupply will update when OGA releases a fix.

## Requirements

- .NET 10.0+
- ONNX Runtime GenAI 0.7+ (for ONNX models)
- llama-server (auto-downloaded for GGUF models)
- Windows, Linux, or macOS

## License

MIT License
