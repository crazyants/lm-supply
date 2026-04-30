using System.Text.Json;
using FluentAssertions;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.ChatFormatters;
using LMSupply.Generator.Internal.Llama;
using LMSupply.Generator.Models;

namespace LMSupply.Generator.Tests.Internal.Llama;

/// <summary>
/// Tests for internal static helper methods on LlamaServerGeneratorModel.
/// These test the VRAM-adaptive logic without requiring actual model files or servers.
/// </summary>
public class LlamaServerGeneratorModelHelperTests
{
    // ─── IsOomError ───

    [Theory]
    [InlineData("CUDA error: out of memory")]
    [InlineData("CUDA_ERROR_OUT_OF_MEMORY")]
    [InlineData("Could not allocate 2048 MiB")]
    [InlineData("ggml_backend_cuda_buffer_type_alloc_buffer: failed to alloc")]
    [InlineData("Error: Out of Memory while loading model")]
    public void IsOomError_TrueForGpuOomMessages(string message)
    {
        var ex = new InvalidOperationException(message);
        LlamaServerGeneratorModel.IsOomError(ex).Should().BeTrue();
    }

    [Theory]
    [InlineData("Connection refused")]
    [InlineData("File not found")]
    [InlineData("Invalid model format")]
    [InlineData("Server startup timeout")]
    [InlineData("")]
    public void IsOomError_FalseForNonOomErrors(string message)
    {
        var ex = new InvalidOperationException(message);
        LlamaServerGeneratorModel.IsOomError(ex).Should().BeFalse();
    }

    // ─── EstimateTotalLayers ───

    [Theory]
    [InlineData(1L * 1024 * 1024 * 1024, 22)]      // <2GB → 22 layers
    [InlineData(3L * 1024 * 1024 * 1024, 28)]      // 2-5GB → 28 layers
    [InlineData(7L * 1024 * 1024 * 1024, 32)]      // 5-10GB → 32 layers
    [InlineData(15L * 1024 * 1024 * 1024, 40)]     // >10GB → 40 layers
    public void EstimateTotalLayers_ReturnsExpectedForFileSize(long fileSize, int expectedLayers)
    {
        LlamaServerGeneratorModel.EstimateTotalLayers(fileSize).Should().Be(expectedLayers);
    }

    [Fact]
    public void EstimateTotalLayers_BoundaryValues()
    {
        // Exactly 2GB boundary
        var just_under_2gb = 2L * 1024 * 1024 * 1024 - 1;
        var exactly_2gb = 2L * 1024 * 1024 * 1024;

        LlamaServerGeneratorModel.EstimateTotalLayers(just_under_2gb).Should().Be(22);
        LlamaServerGeneratorModel.EstimateTotalLayers(exactly_2gb).Should().Be(28);
    }

    // ─── MaybeInjectToolPromptFragment (Option D-1, 2026-04-30) ───

    private static JsonElement BuildSchema(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void MaybeInjectToolPromptFragment_FormatterReturnsFragment_PrependsSystemMessage()
    {
        var formatter = new Gemma4ChatFormatter();
        var schema = BuildSchema("""{ "type":"object", "properties":{ "path":{"type":"string"} }, "required":["path"] }""");
        var tools = new[] { new ChatToolDefinition("WriteFile", "Write a file", schema) };
        var original = new[]
        {
            ChatMessage.System("You are helpful."),
            ChatMessage.User("write /tmp/x")
        };

        var result = LlamaServerGeneratorModel.MaybeInjectToolPromptFragment(original, tools, formatter).ToList();

        result.Should().HaveCount(3,
            because: "fragment is prepended as a new system message — original 2 messages stay");
        result[0].Role.Should().Be(ChatRole.System);
        result[0].Content.Should().Contain("Required parameters",
            because: "the prepended message must carry Gemma4's textual reinforcement so llama-server forwards it to the model");
        result[1].Should().Be(original[0],
            because: "original messages are passed through unchanged");
        result[2].Should().Be(original[1]);
    }

    [Fact]
    public void MaybeInjectToolPromptFragment_FormatterReturnsNull_PassesMessagesThrough()
    {
        // Phi3 doesn't opt in to schema reinforcement.
        var formatter = new Phi3ChatFormatter();
        var schema = BuildSchema("""{ "type":"object", "properties":{ "p":{"type":"string"} }, "required":["p"] }""");
        var tools = new[] { new ChatToolDefinition("AnyTool", "any", schema) };
        var original = new[]
        {
            ChatMessage.System("sys"),
            ChatMessage.User("u")
        };

        var result = LlamaServerGeneratorModel.MaybeInjectToolPromptFragment(original, tools, formatter).ToList();

        result.Should().HaveCount(2,
            because: "Phi3 returned null — pipeline must not synthesize an empty system message");
        result[0].Should().Be(original[0]);
        result[1].Should().Be(original[1]);
    }

    [Fact]
    public void MaybeInjectToolPromptFragment_NullTools_PassesMessagesThrough()
    {
        var formatter = new Gemma4ChatFormatter();
        var original = new[] { ChatMessage.User("hello") };

        var result = LlamaServerGeneratorModel.MaybeInjectToolPromptFragment(original, null, formatter).ToList();

        result.Should().HaveCount(1);
        result[0].Should().Be(original[0]);
    }

    [Fact]
    public void MaybeInjectToolPromptFragment_EmptyTools_PassesMessagesThrough()
    {
        var formatter = new Gemma4ChatFormatter();
        var original = new[] { ChatMessage.User("hello") };

        var result = LlamaServerGeneratorModel.MaybeInjectToolPromptFragment(
            original, Array.Empty<ChatToolDefinition>(), formatter).ToList();

        result.Should().HaveCount(1);
    }
}

/// <summary>
/// Tests for MemoryEstimator.EstimateForGguf partial offload calculations.
/// </summary>
public class MemoryEstimatorPartialOffloadTests
{
    [Fact]
    public void EstimateForGguf_SmallVram_RecommendsPartialLayers()
    {
        // 8GB model, only 4GB VRAM available → partial offload
        var estimate = MemoryEstimator.EstimateForGguf(
            modelFileSizeBytes: 8L * 1024 * 1024 * 1024,
            contextLength: 4096,
            availableVramBytes: 4L * 1024 * 1024 * 1024,
            availableRamBytes: 32L * 1024 * 1024 * 1024);

        estimate.CanFitInVram.Should().BeFalse("8GB model + KV cache exceeds 4GB VRAM");
        estimate.RecommendedGpuLayers.Should().BeGreaterThan(0, "some layers should fit in 4GB");
        estimate.RecommendedGpuLayers.Should().BeLessThan(estimate.TotalLayers, "not all layers fit");
    }

    [Fact]
    public void EstimateForGguf_AmpleVram_RecommendsAllLayers()
    {
        // 3GB model, 24GB VRAM → all layers on GPU
        var estimate = MemoryEstimator.EstimateForGguf(
            modelFileSizeBytes: 3L * 1024 * 1024 * 1024,
            contextLength: 4096,
            availableVramBytes: 24L * 1024 * 1024 * 1024,
            availableRamBytes: 64L * 1024 * 1024 * 1024);

        estimate.CanFitInVram.Should().BeTrue();
        estimate.RecommendedGpuLayers.Should().Be(estimate.TotalLayers);
    }

    [Fact]
    public void EstimateForGguf_NoVram_RecommendsZeroLayers()
    {
        var estimate = MemoryEstimator.EstimateForGguf(
            modelFileSizeBytes: 3L * 1024 * 1024 * 1024,
            contextLength: 4096,
            availableVramBytes: null,
            availableRamBytes: 16L * 1024 * 1024 * 1024);

        estimate.RecommendedGpuLayers.Should().Be(0);
        estimate.CanFitInVram.Should().BeFalse();
    }

    [Fact]
    public void EstimateForGguf_LargeContext_IncreasesMemory()
    {
        var small = MemoryEstimator.EstimateForGguf(
            modelFileSizeBytes: 5L * 1024 * 1024 * 1024,
            contextLength: 4096,
            availableVramBytes: 8L * 1024 * 1024 * 1024);

        var large = MemoryEstimator.EstimateForGguf(
            modelFileSizeBytes: 5L * 1024 * 1024 * 1024,
            contextLength: 32768,
            availableVramBytes: 8L * 1024 * 1024 * 1024);

        // Larger context needs more memory, so may not fit while smaller does
        large.TotalMemoryBytes.Should().BeGreaterThanOrEqualTo(small.TotalMemoryBytes);
    }

    [Fact]
    public void EstimateForGguf_VramBudget_PartialOffload_LayerCountDecreases()
    {
        // Same model, decreasing VRAM → decreasing recommended layers
        var layers8gb = MemoryEstimator.EstimateForGguf(
            modelFileSizeBytes: 8L * 1024 * 1024 * 1024,
            contextLength: 4096,
            availableVramBytes: 8L * 1024 * 1024 * 1024).RecommendedGpuLayers;

        var layers4gb = MemoryEstimator.EstimateForGguf(
            modelFileSizeBytes: 8L * 1024 * 1024 * 1024,
            contextLength: 4096,
            availableVramBytes: 4L * 1024 * 1024 * 1024).RecommendedGpuLayers;

        layers4gb.Should().BeLessThanOrEqualTo(layers8gb,
            "less VRAM should recommend fewer or equal GPU layers");
    }

    [Fact]
    public void EstimateForGguf_GpuOffloadRatio_IsValid()
    {
        var estimate = MemoryEstimator.EstimateForGguf(
            modelFileSizeBytes: 5L * 1024 * 1024 * 1024,
            contextLength: 4096,
            availableVramBytes: 4L * 1024 * 1024 * 1024);

        estimate.GpuOffloadRatio.Should().BeInRange(0f, 1f);
    }
}
