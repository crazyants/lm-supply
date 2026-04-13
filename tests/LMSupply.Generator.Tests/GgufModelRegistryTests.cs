using FluentAssertions;
using LMSupply.Generator.Internal.Llama;
using LMSupply.Runtime;
using Xunit;

namespace LMSupply.Generator.Tests;

public class GgufModelRegistryTests
{
    [Theory]
    [InlineData("gguf:default")]
    [InlineData("gguf:fast")]
    [InlineData("gguf:quality")]
    [InlineData("gguf:balanced")]
    [InlineData("gguf:large")]
    [InlineData("gguf:xlarge")]
    public void Resolve_WithPrefixedAlias_ReturnsModelInfo(string alias)
    {
        var result = GgufModelRegistry.Resolve(alias);

        result.Should().NotBeNull();
        result!.RepoId.Should().NotBeNullOrWhiteSpace();
        result.DefaultFile.Should().EndWith(".gguf");
        result.ChatFormat.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("default")]
    [InlineData("fast")]
    [InlineData("quality")]
    public void Resolve_WithoutPrefix_ReturnsModelInfo(string alias)
    {
        var result = GgufModelRegistry.Resolve(alias);

        result.Should().NotBeNull();
        result!.RepoId.Should().Contain("/");
    }

    [Theory]
    [InlineData("unknown-model")]
    [InlineData("nonexistent")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_WithInvalidAlias_ReturnsNull(string? alias)
    {
        var result = GgufModelRegistry.Resolve(alias!);

        result.Should().BeNull();
    }

    [Fact]
    public void GetAllModels_ReturnsNonEmptyList()
    {
        var models = GgufModelRegistry.GetAllModels();

        models.Should().NotBeEmpty();
        models.Should().AllSatisfy(m =>
        {
            m.RepoId.Should().NotBeNullOrWhiteSpace();
            m.DefaultFile.Should().EndWith(".gguf");
            m.ContextLength.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void DefaultModel_HasValidConfiguration()
    {
        var model = GgufModelRegistry.Resolve("gguf:default");

        model.Should().NotBeNull();
        model!.RepoId.Should().Contain("gemma-4");
        model.ChatFormat.Should().Be("gemma4");
        model.DefaultFile.Should().Contain("Q4_K_M");
        model.ContextLength.Should().BeGreaterThanOrEqualTo(4096);
    }

    [Fact]
    public void AllModels_HaveValidChatFormats()
    {
        var validFormats = new[] { "chatml", "gemma", "gemma4" };
        var models = GgufModelRegistry.GetAllModels();

        models.Should().AllSatisfy(m =>
        {
            validFormats.Should().Contain(m.ChatFormat,
                $"Model {m.DisplayName} has unexpected chat format: {m.ChatFormat}");
        });
    }

    [Fact]
    public void GetAliases_ReturnsExpectedAliases()
    {
        var aliases = GgufModelRegistry.GetAliases();

        aliases.Should().Contain("gguf:default");
        aliases.Should().Contain("gguf:fast");
        aliases.Should().Contain("gguf:quality");
        aliases.Should().Contain("gguf:balanced");
        aliases.Should().Contain("gguf:large");
        aliases.Should().Contain("gguf:xlarge");
    }

    [Fact]
    public void GetAutoModel_LargeVram_SelectsLargestFitting()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 24L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 22L * 1024 * 1024 * 1024
        };

        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.Should().NotBeNull();
    }

    [Fact]
    public void GetAutoModel_LargeVram_SelectsLarge()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 24L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 24L * 1024 * 1024 * 1024
        };
        // 24GB × 0.85 = 20.4GB → gguf:large (18.7GB Gemma 4 31B) fits
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.ParameterCount.Should().Be(31_000_000_000);
    }

    [Fact]
    public void GetAutoModel_12GBVram_SelectsBalanced()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 12L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 11L * 1024 * 1024 * 1024
        };
        // 11GB × 0.85 = 9.35GB → gguf:balanced (7.5GB E4B Q8_0) fits, quality (16.8GB) doesn't
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.QuantizationType.Should().Be("Q8_0");
        model.ParameterCount.Should().Be(4_500_000_000);
    }

    [Fact]
    public void GetAutoModel_MediumVram_SelectsDefault()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 8L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 7L * 1024 * 1024 * 1024
        };
        // 7GB × 0.85 = 5.95GB → gguf:default (5.34GB Gemma 4 E4B Q4_K_M) fits, balanced (7.5GB) doesn't
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.QuantizationType.Should().Be("Q4_K_M");
        model.ParameterCount.Should().Be(4_500_000_000);
    }

    [Fact]
    public void GetAutoModel_TinyVram_SelectsSmallest()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Intel,
            TotalMemoryBytes = 2L * 1024 * 1024 * 1024
        };
        // 2GB × 0.85 = 1.7GB → gguf:fast (3.1GB) doesn't fit → still returns smallest
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.ParameterCount.Should().Be(2_300_000_000);
    }

    [Fact]
    public void GetAutoModel_CpuOnly_SelectsSmallest()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Unknown
        };
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.ParameterCount.Should().Be(2_300_000_000);
    }

    [Fact]
    public void XLargeModel_HasSplitShardConfiguration()
    {
        var model = GgufModelRegistry.Resolve("gguf:xlarge");
        model.Should().NotBeNull();
        model!.ShardCount.Should().Be(3);
        model.DefaultFile.Should().Contain("-00001-of-00003");
        model.DefaultFile.Should().StartWith("Q4_K_M/");
    }

    [Theory]
    [InlineData("gguf:default", true)]
    [InlineData("gguf:fast", true)]
    [InlineData("gguf:quality", true)]
    [InlineData("gguf:xlarge", true)]
    [InlineData("default", false)] // Plain aliases are reserved for ONNX
    [InlineData("fast", false)]    // Plain aliases are reserved for ONNX
    [InlineData("unknown", false)]
    public void IsAlias_ReturnsCorrectResult(string value, bool expected)
    {
        var result = GgufModelRegistry.IsAlias(value);

        result.Should().Be(expected);
    }
}
