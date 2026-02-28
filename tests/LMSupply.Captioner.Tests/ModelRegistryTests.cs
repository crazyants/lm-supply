using FluentAssertions;
using LMSupply.Captioner.Models;
using LMSupply.Exceptions;
using LMSupply.Vision;

namespace LMSupply.Captioner.Tests;

public class ModelRegistryTests
{
    [Fact]
    public void Resolve_WithDefaultAlias_ShouldReturnVitGpt2()
    {
        // Act
        var modelInfo = CaptionerModelRegistry.Default.Resolve("default");

        // Assert
        modelInfo.Should().NotBeNull();
        modelInfo.RepoId.Should().Be("Xenova/vit-gpt2-image-captioning");
    }

    [Fact]
    public void TryResolve_WithFastAlias_ShouldReturnGitBase()
    {
        // Act
        var result = CaptionerModelRegistry.Default.TryResolve("fast", out var modelInfo);

        // Assert
        result.Should().BeTrue();
        modelInfo.Should().NotBeNull();
        modelInfo!.RepoId.Should().Be("Xenova/git-base-coco");
    }

    [Fact]
    public void TryResolve_WithQualityAlias_ShouldReturnBlipBase()
    {
        // Act
        var result = CaptionerModelRegistry.Default.TryResolve("quality", out var modelInfo);

        // Assert
        result.Should().BeTrue();
        modelInfo.Should().NotBeNull();
        modelInfo!.RepoId.Should().Be("Xenova/blip-image-captioning-base");
    }

    [Fact]
    public void TryResolve_WithLargeAlias_ShouldReturnBlipLarge()
    {
        // Act
        var result = CaptionerModelRegistry.Default.TryResolve("large", out var modelInfo);

        // Assert
        result.Should().BeTrue();
        modelInfo.Should().NotBeNull();
        modelInfo!.RepoId.Should().Be("Xenova/blip-image-captioning-large");
    }

    [Fact]
    public void TryResolve_WithRepoId_ShouldReturnModel()
    {
        // Act
        var result = CaptionerModelRegistry.Default.TryResolve("Xenova/vit-gpt2-image-captioning", out var modelInfo);

        // Assert
        result.Should().BeTrue();
        modelInfo.Should().NotBeNull();
        modelInfo!.AliasName.Should().Be("default");
    }

    [Fact]
    public void TryResolve_CaseInsensitive_ShouldWork()
    {
        // Act
        var result1 = CaptionerModelRegistry.Default.TryResolve("DEFAULT", out var model1);
        var result2 = CaptionerModelRegistry.Default.TryResolve("Default", out var model2);
        var result3 = CaptionerModelRegistry.Default.TryResolve("default", out var model3);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
        result3.Should().BeTrue();
        model1!.RepoId.Should().Be(model2!.RepoId);
        model2!.RepoId.Should().Be(model3!.RepoId);
    }

    [Fact]
    public void TryResolve_WithUnknownModel_ShouldReturnFalse()
    {
        // Act
        var result = CaptionerModelRegistry.Default.TryResolve("unknown-model", out var modelInfo);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Resolve_WithUnknownModel_ShouldThrow()
    {
        // Act
        var act = () => CaptionerModelRegistry.Default.Resolve("unknown-model");

        // Assert
        act.Should().Throw<ModelNotFoundException>()
            .WithMessage("*unknown-model*not found*");
    }

    [Fact]
    public void TryResolve_WithShortName_ShouldResolve()
    {
        // Act - short name is the part after '/' in the repo ID
        var result = CaptionerModelRegistry.Default.TryResolve("vit-gpt2-image-captioning", out var modelInfo);

        // Assert
        result.Should().BeTrue();
        modelInfo.Should().NotBeNull();
        modelInfo!.RepoId.Should().Be("Xenova/vit-gpt2-image-captioning");
    }

    [Fact]
    public void TryResolve_WithAutoAlias_ShouldReturnHardwareAdaptive()
    {
        // Act
        var result = CaptionerModelRegistry.Default.TryResolve("auto", out var modelInfo);

        // Assert
        result.Should().BeTrue();
        modelInfo.Should().NotBeNull();
        modelInfo!.AliasName.Should().Be("auto");
    }

    [Fact]
    public void GetAliases_ShouldContainStandardAliases()
    {
        // Act
        var aliases = CaptionerModelRegistry.Default.GetAliases();

        // Assert
        var aliasNames = aliases.Select(a => a.Name).ToList();
        aliasNames.Should().Contain("auto");
        aliasNames.Should().Contain("default");
        aliasNames.Should().Contain("fast");
        aliasNames.Should().Contain("quality");
        aliasNames.Should().Contain("large");
    }

    [Fact]
    public void GetAvailableModels_ShouldReturnAllRegisteredModels()
    {
        // Act
        var models = CaptionerModelRegistry.Default.GetAvailableModels();

        // Assert
        models.Should().HaveCount(4);
        models.Select(m => m.RepoId).Should().Contain("Xenova/vit-gpt2-image-captioning");
        models.Select(m => m.RepoId).Should().Contain("Xenova/git-base-coco");
        models.Select(m => m.RepoId).Should().Contain("Xenova/blip-image-captioning-base");
        models.Select(m => m.RepoId).Should().Contain("Xenova/blip-image-captioning-large");
    }

    [Fact]
    public void RegisterAlias_ShouldCreateNewMapping()
    {
        // Arrange
        var registry = new CaptionerModelRegistry(DefaultModels.All);

        // Act
        registry.RegisterAlias("my-custom", "Xenova/vit-gpt2-image-captioning");
        var result = registry.TryResolve("my-custom", out var model);

        // Assert
        result.Should().BeTrue();
        model!.RepoId.Should().Be("Xenova/vit-gpt2-image-captioning");
    }

    [Fact]
    public void RegisterAlias_WithSystemAliasName_ShouldThrow()
    {
        // Arrange
        var registry = new CaptionerModelRegistry(DefaultModels.All);

        // Act
        var act = () => registry.RegisterAlias("default", "Xenova/vit-gpt2-image-captioning");

        // Assert
        act.Should().Throw<AliasConflictException>();
    }

    [Fact]
    public void VitGpt2Model_ShouldHaveCorrectConfiguration()
    {
        // Act
        var model = CaptionerModelRegistry.Default.Resolve("default");

        // Assert
        model.EncoderFile.Should().Be("encoder_model.onnx");
        model.DecoderFile.Should().Be("decoder_model_merged.onnx");
        model.TokenizerType.Should().Be(TokenizerType.Gpt2);
        model.SupportsVqa.Should().BeFalse();
        model.VocabSize.Should().Be(50257);
        model.BosTokenId.Should().Be(50256);
        model.EosTokenId.Should().Be(50256);
        model.PadTokenId.Should().Be(50256);
    }

    [Fact]
    public void VitGpt2Model_ShouldUseViTGpt2PreprocessProfile()
    {
        // Act
        var model = CaptionerModelRegistry.Default.Resolve("default");

        // Assert
        model.PreprocessProfile.Width.Should().Be(224);
        model.PreprocessProfile.Height.Should().Be(224);
    }

    [Fact]
    public void DefaultModels_All_ShouldContainAllModels()
    {
        // Assert
        DefaultModels.All.Should().HaveCount(4);
        DefaultModels.All.Should().Contain(DefaultModels.VitGpt2);
        DefaultModels.All.Should().Contain(DefaultModels.GitBase);
        DefaultModels.All.Should().Contain(DefaultModels.BlipBase);
        DefaultModels.All.Should().Contain(DefaultModels.BlipLarge);
    }

    [Fact]
    public void DefaultModels_Default_ShouldBeVitGpt2()
    {
        // Assert
        DefaultModels.Default.Should().Be(DefaultModels.VitGpt2);
    }

    [Fact]
    public void LocalCaptioner_Registry_ShouldExposeRegistry()
    {
        // Act
        var registry = LocalCaptioner.Registry;

        // Assert
        registry.Should().NotBeNull();
        registry.Should().BeOfType<CaptionerModelRegistry>();
    }

    [Fact]
    public void TryResolve_WithHuggingFaceRepoId_ShouldCreateFallback()
    {
        // Act - unknown HF repo should create a fallback
        var result = CaptionerModelRegistry.Default.TryResolve("my-org/my-captioner", out var modelInfo);

        // Assert
        result.Should().BeTrue();
        modelInfo.Should().NotBeNull();
        modelInfo!.RepoId.Should().Be("my-org/my-captioner");
        modelInfo.Description.Should().Contain("Custom model");
    }
}
