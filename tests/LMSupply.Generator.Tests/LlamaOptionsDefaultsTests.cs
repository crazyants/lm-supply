using FluentAssertions;
using LMSupply.Hardware;

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
}
