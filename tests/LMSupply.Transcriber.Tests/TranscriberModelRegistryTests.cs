using FluentAssertions;
using LMSupply;
using LMSupply.Exceptions;
using LMSupply.Transcriber.Models;

namespace LMSupply.Transcriber.Tests;

public class TranscriberModelRegistryTests
{
    [Fact]
    public void Default_ShouldNotBeNull()
    {
        TranscriberModelRegistry.Default.Should().NotBeNull();
    }

    [Fact]
    public void GetAliases_ShouldReturnStandardAliases()
    {
        var aliases = TranscriberModelRegistry.Default.GetAliases();
        var aliasNames = aliases.Select(a => a.Name).ToList();

        aliasNames.Should().Contain("default");
        aliasNames.Should().Contain("fast");
        aliasNames.Should().Contain("quality");
    }

    [Fact]
    public void GetAvailableModels_ShouldReturnAllModels()
    {
        var models = TranscriberModelRegistry.Default.GetAvailableModels();

        models.Should().NotBeEmpty();
        models.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("fast")]
    [InlineData("quality")]
    [InlineData("large")]
    public void TryResolve_ValidAlias_ShouldReturnModel(string alias)
    {
        var result = TranscriberModelRegistry.Default.TryResolve(alias, out var model);

        result.Should().BeTrue();
        model.Should().NotBeNull();
        model!.AliasName.Should().Be(alias);
    }

    [Fact]
    public void TryResolve_InvalidAlias_ShouldReturnFalse()
    {
        var result = TranscriberModelRegistry.Default.TryResolve("nonexistent-model", out var model);

        result.Should().BeFalse();
        model.Should().BeNull();
    }

    [Fact]
    public void TryResolve_ByModelId_ShouldReturnModel()
    {
        var allModels = TranscriberModelRegistry.Default.GetAvailableModels();
        var firstModel = allModels[0];

        var result = TranscriberModelRegistry.Default.TryResolve(firstModel.Id, out var model);

        result.Should().BeTrue();
        model.Should().NotBeNull();
        model!.Id.Should().Be(firstModel.Id);
    }

    [Fact]
    public void TryResolve_ShouldBeCaseInsensitive()
    {
        var resultLower = TranscriberModelRegistry.Default.TryResolve("default", out var modelLower);
        var resultUpper = TranscriberModelRegistry.Default.TryResolve("DEFAULT", out var modelUpper);
        var resultMixed = TranscriberModelRegistry.Default.TryResolve("Default", out var modelMixed);

        resultLower.Should().BeTrue();
        resultUpper.Should().BeTrue();
        resultMixed.Should().BeTrue();
        modelLower.Should().BeEquivalentTo(modelUpper);
        modelUpper.Should().BeEquivalentTo(modelMixed);
    }

    /// <summary>
    /// Verifies that the "large" alias points to whisper-large-v3-turbo (public model),
    /// not the gated whisper-large-v3 which requires HuggingFace authentication.
    /// Fix for issue #4: Large model returns 401 Unauthorized error.
    /// </summary>
    [Fact]
    public void TryResolve_LargeAlias_ShouldResolveToTurboModel()
    {
        var result = TranscriberModelRegistry.Default.TryResolve("large", out var model);

        result.Should().BeTrue();
        model.Should().NotBeNull();
        model!.Id.Should().Contain("turbo", "large alias should use turbo model to avoid gated model auth issues");
        model.Id.Should().NotBe("onnx-community/whisper-large-v3", "gated model requires HuggingFace auth");
    }

    /// <summary>
    /// Verifies all registered models use public HuggingFace repositories
    /// that don't require authentication for download.
    /// </summary>
    [Fact]
    public void GetAvailableModels_AllModels_ShouldUsePublicRepositories()
    {
        var models = TranscriberModelRegistry.Default.GetAvailableModels();

        // Known gated models that should NOT be used
        var gatedModels = new[]
        {
            "onnx-community/whisper-large-v3",
            "openai/whisper-large-v3"
        };

        foreach (var model in models)
        {
            gatedModels.Should().NotContain(model.Id,
                $"Model '{model.AliasName}' uses gated repository '{model.Id}' which requires HuggingFace auth");
        }
    }

    [Fact]
    public void Resolve_AutoAlias_ShouldReturnModel()
    {
        var model = TranscriberModelRegistry.Default.Resolve("auto");

        model.Should().NotBeNull();
        model.AliasName.Should().Be("auto");
    }

    [Fact]
    public void Resolve_UnknownAlias_ShouldThrow()
    {
        var act = () => TranscriberModelRegistry.Default.Resolve("nonexistent");

        act.Should().Throw<ModelNotFoundException>()
            .Where(e => e.ModelId == "nonexistent");
    }

    [Fact]
    public void RegisterAlias_ShouldBeResolvable()
    {
        var registry = new TranscriberModelRegistry(DefaultModels.All);

        registry.RegisterAlias("my-whisper", DefaultModels.WhisperBase.Id);

        var model = registry.Resolve("my-whisper");
        model.Should().NotBeNull();
        model.Id.Should().Be(DefaultModels.WhisperBase.Id);
    }

    [Fact]
    public void RegisterAlias_SystemAliasConflict_ShouldThrow()
    {
        var registry = new TranscriberModelRegistry(DefaultModels.All);

        var act = () => registry.RegisterAlias("default", DefaultModels.WhisperBase.Id);

        act.Should().Throw<AliasConflictException>();
    }

    [Fact]
    public void LocalTranscriber_Registry_ShouldExpose()
    {
        var registry = LocalTranscriber.Registry;

        registry.Should().NotBeNull();
        registry.Should().BeAssignableTo<IModelRegistry<TranscriberModelInfo>>();
    }
}
