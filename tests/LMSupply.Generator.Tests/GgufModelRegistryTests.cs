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

    [Theory]
    [InlineData("gguf:fast")]
    [InlineData("gguf:default")]
    [InlineData("gguf:balanced")]
    [InlineData("gguf:quality")]
    [InlineData("gguf:large")]
    public void RegisteredGemmaModels_HaveArchitectureFields(string alias)
    {
        var model = GgufModelRegistry.Resolve(alias);

        model.Should().NotBeNull();
        model!.NumLayers.Should().BeGreaterThan(0,
            because: $"{alias} must declare NumLayers for KV cache budgeting");
        model.HiddenSize.Should().BeGreaterThan(0,
            because: $"{alias} must declare HiddenSize for KV cache budgeting");
    }

    [Fact]
    public void GetAutoSelection_ReturnsResultWithCandidatesAndReason()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            DeviceName = "Test GPU",
            TotalMemoryBytes = 12L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 11L * 1024 * 1024 * 1024,
        };

        var result = GgufModelRegistry.GetAutoSelection(gpu);

        result.Should().NotBeNull();
        result.Selected.Should().NotBeNull();
        result.AvailableVramBytes.Should().BeGreaterThan(0);
        result.BudgetContextLength.Should().BeGreaterThan(0);
        result.Reason.Should().BeOneOf(
            ModelSelectionReason.Fits,
            ModelSelectionReason.FallbackToSmallest);
        result.Candidates.Should().NotBeEmpty();
        result.Candidates.Should().AllSatisfy(c =>
        {
            c.Model.Should().NotBeNull();
            c.WeightsBytes.Should().BeGreaterThan(0);
            c.KvCacheBytes.Should().BeGreaterThan(0);
            c.TotalBytes.Should().Be(c.WeightsBytes + c.KvCacheBytes);
        });
    }

    [Fact]
    public void GetAutoSelection_LowVramLaptop_FallsBackWithReason()
    {
        // 4GB Windows NVIDIA laptop: smallest fast (3.1GB weights + ~1GB KV) won't fit.
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            DeviceName = "NVIDIA RTX 4060 Laptop GPU",
            TotalMemoryBytes = 4L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 3L * 1024 * 1024 * 1024,
        };

        var result = GgufModelRegistry.GetAutoSelection(gpu);

        result.Selected.AliasName.Should().Be("gguf:fast",
            because: "smallest available model is the safest fallback");
        // On Windows the recommended margin is 25% for 4GB cards
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            result.Reason.Should().Be(ModelSelectionReason.FallbackToSmallest);
        }
    }

    [Fact]
    public void GetAutoSelection_KvCacheCountedInBudget()
    {
        // 6.5GB total card (budget = 6.5 × 0.85 = 5.53 GB, non-low-VRAM margin).
        // gguf:default weights = 5.3GB, KV @ 4096 ctx ≈ 1.4GB → total ~6.7GB (over budget)
        // gguf:qwen2.5-7b weights = 4.7GB, KV @ 4096 ctx ≈ 1.6GB → total ~6.3GB (over budget)
        // → must demote to gguf:fast (3.1GB + ~0.9GB KV ≈ 4.0GB).
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            DeviceName = "Test 6.5GB",
            TotalMemoryBytes = (long)(6.5 * 1024L * 1024 * 1024),
            FreeMemoryBytes = (long)(5.5 * 1024L * 1024 * 1024),
        };

        var result = GgufModelRegistry.GetAutoSelection(gpu);

        result.Selected.AliasName.Should().Be("gguf:fast",
            because: "with KV cache @ 4096 included, default (E4B) and qwen2.5-7b both exceed 5.53 GB budget");
        result.Reason.Should().Be(ModelSelectionReason.Fits);
    }

    [Fact]
    public void Resolve_PopulatesAliasName()
    {
        var model = GgufModelRegistry.Resolve("gguf:default");
        model.Should().NotBeNull();
        model!.AliasName.Should().Be("gguf:default");
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
    public void GetAutoModel_LargeVram_SelectsQuality()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 24L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 24L * 1024 * 1024 * 1024
        };
        // 24GB × 0.85 = 20.4GB budget. Including KV @ 4096:
        // - large (18.7GB + ~5.1GB KV) = 23.8GB → does NOT fit
        // - quality (16.8GB + ~2.9GB KV) = 19.7GB → fits
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.ParameterCount.Should().Be(26_000_000_000);
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
        // 11GB × 0.85 = 9.35GB. Balanced (7.5GB Q8_0 + ~1.4GB KV @ 4096 ≈ 8.9GB) fits.
        // Quality (16.8GB + KV) doesn't fit.
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.QuantizationType.Should().Be("Q8_0");
        model.ParameterCount.Should().Be(4_500_000_000);
    }

    [Fact]
    public void GetAutoModel_MediumVram_SelectsFast()
    {
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 6L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 5L * 1024 * 1024 * 1024
        };
        // 6GB × 0.85 = 5.10GB (Linux); 6GB × 0.75 = 4.50GB (Windows low-VRAM margin, ≤6GB).
        // In both cases qwen2.5-7b (~6.3GB total) and gguf:default (~6.7GB total) don't fit.
        // fast (3.1GB + ~0.9GB KV ≈ 4.0GB) fits.
        var model = GgufModelRegistry.GetAutoModel(gpu);
        model.QuantizationType.Should().Be("Q4_K_M");
        model.ParameterCount.Should().Be(2_300_000_000);
    }

    [Fact]
    public void GetAutoModel_8GBVram_SelectsDefault()
    {
        // 8GB total × 0.85 = 6.8GB budget.
        // - default (5.3GB + ~1.4GB KV ≈ 6.7GB) fits
        // - balanced (7.5GB + ~1.4GB KV ≈ 8.9GB) does NOT fit
        var gpu = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 8L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 6L * 1024 * 1024 * 1024
        };
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

    [Fact]
    public void GetAutoSelection_AliasName_IsResolvable()
    {
        // Regression for v0.29.0 → v0.30.0 fix: LoadAutoAsync passes
        // selection.Selected.AliasName (not RepoId) downstream so the loader can
        // re-resolve it to the registry entry's DefaultFile. If AliasName is
        // empty or not in the registry, the loader falls through to
        // GgufFileSelector which can pick bf16 on small-VRAM hosts.
        var lowVram = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 4L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 3L * 1024 * 1024 * 1024,
        };
        var highVram = new GpuInfo
        {
            Vendor = GpuVendor.Nvidia,
            TotalMemoryBytes = 24L * 1024 * 1024 * 1024,
            FreeMemoryBytes = 22L * 1024 * 1024 * 1024,
        };

        foreach (var gpu in new[] { lowVram, highVram })
        {
            var selection = GgufModelRegistry.GetAutoSelection(gpu);
            selection.Selected.AliasName.Should().NotBeNullOrEmpty(
                because: "LoadAutoAsync needs an alias to round-trip through Resolve");

            var roundTripped = GgufModelRegistry.Resolve(selection.Selected.AliasName);
            roundTripped.Should().NotBeNull(
                because: "the alias from GetAutoSelection must be resolvable via Resolve");
            roundTripped!.DefaultFile.Should().Be(selection.Selected.DefaultFile,
                because: "round-trip must preserve the exact DefaultFile to avoid bf16 fallback");
        }
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
