# LMSupply.Llama

Shared llama-server management for GGUF model support in LMSupply.

## Overview

`LMSupply.Llama` provides centralized management of [llama-server](https://github.com/ggml-org/llama.cpp) for GGUF model support across LMSupply packages (Generator, Embedder, Reranker).

This package follows LMSupply's on-demand philosophy: native binaries are downloaded only when first needed, with automatic GPU backend detection and fallback.

## Architecture

LMSupply.Llama uses llama-server as a unified backend for all GGUF operations:

```
LMSupply.Llama/Server/
├── LlamaServerDownloader.cs   - Downloads binaries from GitHub releases
├── LlamaServerPool.cs         - Manages server instance pooling
├── LlamaServerProcess.cs      - Server process lifecycle management
├── LlamaServerClient.cs       - HTTP client for API calls
└── LlamaServerStateManager.cs - Server state and health tracking
```

### Server Modes

llama-server operates in different modes depending on the use case:

| Mode | Flag | Use Case | Consumer Package |
|------|------|----------|------------------|
| Generation | (default) | Text generation | LMSupply.Generator |
| Embedding | `--embedding` | Vector embeddings | LMSupply.Embedder |
| Reranking | `--embedding --pooling rank` | Document reranking | LMSupply.Reranker |

## Features

- **On-demand binary download**: Native binaries downloaded on first use from GitHub releases
- **Automatic backend selection**: Detects hardware and selects optimal GPU backend
- **Fallback chain**: Automatically falls back to CPU if GPU backends fail
- **Cross-platform**: Windows, Linux, macOS (including Apple Silicon)
- **Server pooling**: Efficient server instance reuse across multiple requests
- **Mode-aware pooling**: Separate server instances for generation, embedding, and reranking

## Supported Backends

| Backend | Platform | Hardware |
|---------|----------|----------|
| `Cuda12/13` | Windows/Linux | NVIDIA GPU with CUDA 12.x/13.x |
| `Vulkan` | Windows/Linux | AMD/Intel/NVIDIA GPU |
| `Metal` | macOS | Apple Silicon (M1/M2/M3/M4) |
| `Hip` | Windows/Linux | AMD ROCm |
| `Cpu` | All | CPU with AVX2/AVX512 optimization |

## Backend Selection

The runtime automatically detects your hardware and selects the best backend:

```
macOS ARM64:  Metal → CPU
NVIDIA GPU:   CUDA 13 → CUDA 12 → Vulkan → CPU
AMD GPU:      Hip → Vulkan → CPU
Intel GPU:    Vulkan → CPU
No GPU:       CPU
```

## Usage

### Generator (Text Generation)

Generator GGUF models use llama-server in default generation mode:

```csharp
using LMSupply.Generator;

// llama-server is automatically downloaded and started
await using var model = await LocalGenerator.LoadAsync("gguf:default");

// Server pooling: same model reuses existing server instance
await using var model2 = await LocalGenerator.LoadAsync("gguf:default");

// Generate text
await foreach (var token in model.GenerateAsync("Hello!"))
{
    Console.Write(token);
}
```

### Embedder (Vector Embeddings)

Embedder GGUF models use llama-server with `--embedding` flag:

```csharp
using LMSupply.Embedder;

// llama-server is automatically started in embedding mode
await using var model = await LocalEmbedder.LoadAsync("nomic-ai/nomic-embed-text-v1.5-GGUF");

// Generate embeddings
float[] embedding = await model.EmbedAsync("Hello!");
float[][] embeddings = await model.EmbedAsync(new[] { "Text 1", "Text 2" });
```

### Reranker (Document Ranking)

Reranker GGUF models use llama-server with `--embedding --pooling rank`:

```csharp
using LMSupply.Reranker;

// llama-server is automatically started in reranking mode
await using var model = await LocalReranker.LoadAsync("BAAI/bge-reranker-v2-m3-GGUF");

// Rerank documents
var results = await model.RerankAsync(
    "What is machine learning?",
    new[] { "ML is a subset of AI...", "Weather is sunny today..." }
);
```

## Server Pooling

The llama-server backend uses intelligent server pooling:

- **Shared instances**: Same model + mode reuses existing server
- **Mode isolation**: Generation, embedding, and reranking use separate servers
- **Automatic cleanup**: Idle servers are stopped after timeout
- **Health monitoring**: Unhealthy servers are restarted
- **Resource management**: Memory-aware server allocation

Pool key format: `{modelPath}|{backend}|{contextSize}|{mode}`

## Advanced Configuration

### LlamaOptions (Generator)

`LlamaOptions` provides fine-grained control over llama-server behavior:

```csharp
using LMSupply.Generator;

var options = new GeneratorOptions
{
    LlamaOptions = new LlamaOptions
    {
        // GPU layers (-1 = all, 0 = CPU only)
        GpuLayerCount = -1,

        // KV cache quantization (reduces VRAM usage by 50-75%)
        TypeK = KvCacheQuantizationType.Q8_0,
        TypeV = KvCacheQuantizationType.Q8_0,

        // Memory options
        UseMemoryMap = true,
        UseMemoryLock = false,

        // Multi-GPU
        MainGpu = 0,

        // Performance
        FlashAttention = true,
        BatchSize = 2048,
        UBatchSize = 512
    }
};

await using var model = await LocalGenerator.LoadAsync("gguf:default", options);
```

### KV Cache Quantization

Quantizing the KV cache can significantly reduce VRAM usage:

| Type | Memory Savings | Quality Impact |
|------|----------------|----------------|
| `F16` (default) | 0% | None |
| `Q8_0` | ~50% | Minimal |
| `Q4_0` | ~75% | Noticeable |
| `F32` | -100% (doubles) | Maximum quality |

### Sampling Parameters

`GenerationOptions` provides comprehensive sampling control:

```csharp
var genOpts = new GenerationOptions
{
    Temperature = 0.7f,
    TopP = 0.9f,
    TopK = 50,
    MinP = 0.05f,
    RepetitionPenalty = 1.1f,
    FrequencyPenalty = 0.0f,
    PresencePenalty = 0.0f,
    Seed = 42
};

await foreach (var token in model.GenerateAsync("Hello!", genOpts))
{
    Console.Write(token);
}
```

### Grammar Constraints

Constrain output to match specific patterns:

```csharp
// GBNF grammar for yes/no answers
var options = new GenerationOptions
{
    Grammar = "root ::= (\"yes\" | \"no\")"
};

// JSON schema constraint
var jsonOptions = new GenerationOptions
{
    JsonSchema = """
    {
        "type": "object",
        "properties": {
            "name": {"type": "string"},
            "age": {"type": "integer"}
        },
        "required": ["name", "age"]
    }
    """
};
```

### Hardware-Optimized Defaults

`LlamaOptions.GetOptimalForHardware()` automatically configures based on your system:

| Hardware Tier | GPU Layers | Batch Size | KV Cache | Flash Attention |
|---------------|------------|------------|----------|-----------------|
| Ultra (32GB+ VRAM) | All | 4096 | Q8_0 | Yes |
| High (12-32GB VRAM) | All | 2048 | Q8_0 | Yes |
| Medium (6-12GB VRAM) | All | 1024 | Q4_0 | No |
| Low (< 6GB or CPU) | 0 | 512 | F16 | No |

```csharp
var options = new GeneratorOptions
{
    LlamaOptions = LlamaOptions.GetOptimalForHardware()
};
```

## Consumer Packages

The following packages use LMSupply.Llama for GGUF support:

| Package | Server Mode | Use Case |
|---------|-------------|----------|
| **LMSupply.Generator** | Generation | GGUF language models (Llama, Qwen, etc.) |
| **LMSupply.Embedder** | Embedding | GGUF embedding models (nomic-embed, etc.) |
| **LMSupply.Reranker** | Reranking | GGUF reranker models (bge-reranker, etc.) |

## Troubleshooting

### Server download failed

- Check network access to GitHub releases
- Verify cache directory permissions
- Try setting `HF_HUB_OFFLINE=0` to force online mode

### Server won't start

- Check if port is already in use (servers use random available ports)
- Verify model file exists and is valid GGUF
- Check system logs for GPU driver issues

### GPU not detected

- Install appropriate GPU drivers
- For CUDA: Ensure NVIDIA drivers are installed
- For Vulkan: Install Vulkan runtime
- For Metal: macOS 11+ required

### Force CPU backend

```csharp
var options = new GeneratorOptions { Provider = ExecutionProvider.Cpu };
await using var model = await LocalGenerator.LoadAsync("gguf:default", options);
```

### Clear Cache

Delete cached binaries to force re-download:

```bash
# Windows
del /s /q %LOCALAPPDATA%\LMSupply\cache\llama-server

# Linux/macOS
rm -rf ~/.local/share/LMSupply/cache/llama-server
```

## Version Information

- **llama-server**: Downloaded from [llama.cpp GitHub releases](https://github.com/ggml-org/llama.cpp/releases)
- Binaries are versioned and cached by build number (tag format: `b<NNNN>`, e.g., `b8672`)

### Minimum Version Requirements

`LlamaServerVersionRequirements` validates that the installed llama-server is new enough for the model architecture being loaded. Requirements are keyed by chat format name and checked at model load time.

| Chat Format | Minimum Build | Reason |
|-------------|---------------|--------|
| `gemma4` | `b8672` | Native Gemma 4 architecture support (GGUF metadata auto-detect) |

If the cached binary is too old, `LoadAsync()` throws `InvalidOperationException` with a clear remediation hint:

```
llama-server b7898 is too old for gemma4 models. Minimum required: b8672.
Delete the cached llama-server to trigger a fresh download of the latest version.
```

Validation degrades gracefully — if the version string can't be parsed, model loading is allowed to proceed.

## Split GGUF Downloads

Some large models are distributed as multiple shards using the `-NNNNN-of-NNNNN.gguf` naming convention (e.g., Qwen 3.5 122B comes as 3 parts in a `Q4_K_M/` subfolder). The `GgufModelDownloader` handles this automatically:

- Models in the registry declare `ShardCount` and point `DefaultFile` at the first shard
- All shards are fetched sequentially with progress reporting per shard
- llama-server is started with the first shard path and auto-loads the remaining parts from the same directory
