using FluentAssertions;
using LMSupply.Hardware;

namespace LMSupply.Llama.Tests;

/// <summary>
/// Tests for LlamaServerConfig argument building.
/// </summary>
public class LlamaServerConfigArgsTests
{
    [Fact]
    public void SpeculativeDecodingMode_HasExpectedValues()
    {
        Enum.GetNames<SpeculativeDecodingMode>()
            .Should().Contain(["Auto", "None", "Ngram", "DraftModel"]);
    }

    [Fact]
    public void RopeScalingMode_HasExpectedValues()
    {
        Enum.GetNames<RopeScalingMode>()
            .Should().Contain(["Default", "Linear", "YaRN", "LongRoPE"]);
    }
}
