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
        model!.RepoId.Should().Contain("Hermes");
        model.ChatFormat.Should().Be("chatml");
        model.DefaultFile.Should().Contain("Q4_K_M");
        model.ContextLength.Should().BeGreaterThanOrEqualTo(4096);
    }

    [Fact]
    public void AllModels_HaveValidChatFormats()
    {
        var validFormats = new[] { "chatml", "mistral-nemo" };
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
        // 24GB × 0.85 = 20.4GB → gguf:large (19GB) fits
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.ParameterCount.Should().Be(32_000_000_000);
    }

    [Fact]
    public void GetAutoModel_MediumVram_SelectsDefault()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 8L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 6L * 1024 * 1024 * 1024
        };
        // 6GB × 0.85 = 5.1GB → gguf:default (4.92GB) fits
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.ParameterCount.Should().BeLessThanOrEqualTo(8_000_000_000);
    }

    [Fact]
    public void GetAutoModel_TinyVram_SelectsSmallest()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Intel,
            TotalMemoryBytes = 2L * 1024 * 1024 * 1024
        };
        // 2GB × 0.85 = 1.7GB → gguf:fast (2GB) doesn't fit → still returns smallest
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.ParameterCount.Should().Be(3_000_000_000);
    }

    [Fact]
    public void GetAutoModel_CpuOnly_SelectsSmallest()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Unknown
        };
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.ParameterCount.Should().Be(3_000_000_000);
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
