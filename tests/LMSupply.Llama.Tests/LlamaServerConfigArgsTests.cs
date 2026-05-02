using FluentAssertions;
using LMSupply.Llama.Server;

namespace LMSupply.Llama.Tests;

/// <summary>
/// Tests for LlamaServerConfig argument building.
/// </summary>
public class LlamaServerConfigArgsTests
{
    [Theory]
    [InlineData("spec-ngram",   8500)]
    [InlineData("kv-q8-vulkan", 8500)]
    public void GetMinimumBuild_FeatureKey_ReturnsExpectedBuild(string key, int expected)
    {
        LlamaServerVersionRequirements.GetMinimumBuild(key).Should().Be(expected);
    }
}
