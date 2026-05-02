using FluentAssertions;
using LMSupply.Generator.Internal.Llama;
using LMSupply.Hardware;
using LMSupply.Llama.Server;

namespace LMSupply.Generator.Tests;

public class LlamaOptionsDefaultsTests
{
    [Fact]
    public void LlamaOptions_Default_TypeKIsAuto()
    {
        var opts = new LlamaOptions();
        opts.TypeK.Should().Be(KvCacheQuantizationType.Auto);
    }

    [Fact]
    public void LlamaOptions_Default_TypeVIsAuto()
    {
        var opts = new LlamaOptions();
        opts.TypeV.Should().Be(KvCacheQuantizationType.Auto);
    }

    [Fact]
    public void LlamaOptions_Default_SpeculativeDecodingIsAuto()
    {
        var opts = new LlamaOptions();
        opts.SpeculativeDecoding.Should().Be(SpeculativeDecodingMode.Auto);
    }

    [Fact]
    public void LlamaOptions_Default_RopeScalingIsDefault()
    {
        var opts = new LlamaOptions();
        opts.RopeScaling.Should().Be(RopeScalingMode.Default);
    }

    // ─── ResolveKvCacheType ───────────────────────────────────────────────
    [Theory]
    [InlineData(LlamaServerBackend.Cuda12,  "b8863", "q8_0")]
    [InlineData(LlamaServerBackend.Cuda13,  "b8863", "q8_0")]
    [InlineData(LlamaServerBackend.Metal,   "b8863", "q8_0")]
    [InlineData(LlamaServerBackend.Hip,     "b8863", "q8_0")]
    [InlineData(LlamaServerBackend.Vulkan,  "b8863", "q8_0")]   // ≥ 8500
    [InlineData(LlamaServerBackend.Vulkan,  "b8000", null)]     // < 8500 → F16 = null
    [InlineData(LlamaServerBackend.Cpu,     "b8863", null)]     // CPU → F16 = null
    [InlineData(LlamaServerBackend.Sycl,    "b8863", null)]     // SYCL → F16 = null
    public void ResolveKvCacheType_Auto_ReturnsExpectedString(
        LlamaServerBackend backend, string version, string? expected)
    {
        var result = LlamaServerGeneratorModel.ResolveKvCacheType(
            KvCacheQuantizationType.Auto, backend, version);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(KvCacheQuantizationType.F16,  null)]
    [InlineData(KvCacheQuantizationType.Q8_0, "q8_0")]
    [InlineData(KvCacheQuantizationType.Q4_0, "q4_0")]
    [InlineData(KvCacheQuantizationType.F32,  "f32")]
    public void ResolveKvCacheType_Explicit_ReturnsExpectedString(
        KvCacheQuantizationType type, string? expected)
    {
        var result = LlamaServerGeneratorModel.ResolveKvCacheType(
            type, LlamaServerBackend.Cpu, "b8863");
        result.Should().Be(expected);
    }

    // ─── ResolveSpecType ──────────────────────────────────────────────────
    [Theory]
    [InlineData(SpeculativeDecodingMode.Auto,  "b8863", "ngram")]  // ≥ 8500
    [InlineData(SpeculativeDecodingMode.Auto,  "b8000", null)]     // < 8500 → None
    [InlineData(SpeculativeDecodingMode.Auto,  null,    null)]     // unknown → None
    [InlineData(SpeculativeDecodingMode.Ngram, "b8000", "ngram")]  // explicit Ngram always
    [InlineData(SpeculativeDecodingMode.None,  "b8863", null)]
    public void ResolveSpecType_ReturnsExpectedString(
        SpeculativeDecodingMode mode, string? version, string? expected)
    {
        var result = LlamaServerGeneratorModel.ResolveSpecType(mode, version, null);
        result.Should().Be(expected);
    }
}
