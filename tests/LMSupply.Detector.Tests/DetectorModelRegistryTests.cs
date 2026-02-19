using FluentAssertions;
using LMSupply.Detector.Models;

namespace LMSupply.Detector.Tests;

public class DetectorModelRegistryTests
{
    private readonly DetectorModelRegistry _registry = DetectorModelRegistry.Default;

    [Theory]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    [InlineData("Default")]
    public void Resolve_DefaultAlias_ShouldReturnRtDetrV2S(string alias)
    {
        var model = _registry.Resolve(alias);

        model.Should().NotBeNull();
        model.Id.Should().Be("xnorpx/rt-detr2-onnx:s");
        model.AliasName.Should().Be("default");
    }

    [Theory]
    [InlineData("quality", "xnorpx/rt-detr2-onnx:m")]
    [InlineData("fast", "xnorpx/rt-detr2-onnx:ms")]
    [InlineData("large", "xnorpx/rt-detr2-onnx:l")]
    public void Resolve_BuiltInAliases_ShouldReturnCorrectModel(string alias, string expectedId)
    {
        var model = _registry.Resolve(alias);

        model.Should().NotBeNull();
        model.Id.Should().Be(expectedId);
    }

    [Fact]
    public void Resolve_FullModelId_ShouldReturnModel()
    {
        var model = _registry.Resolve("xnorpx/rt-detr2-onnx:s");

        model.Should().NotBeNull();
        model.DisplayName.Should().Contain("RT-DETR v2");
    }

    [Fact]
    public void Resolve_UnknownHuggingFaceId_ShouldCreateGenericModel()
    {
        var model = _registry.Resolve("some-org/some-detector");

        model.Should().NotBeNull();
        model.Id.Should().Be("some-org/some-detector");
        model.OnnxFile.Should().Be("model.onnx");
    }

    [Fact]
    public void Resolve_LocalPath_ShouldCreateLocalModel()
    {
        var model = _registry.Resolve("./models/custom.onnx");

        model.Should().NotBeNull();
        model.AliasName.Should().Be("local");
        model.OnnxFile.Should().Be("custom.onnx");
    }

    [Fact]
    public void Resolve_UnknownAlias_ShouldThrow()
    {
        var act = () => _registry.Resolve("nonexistent");

        act.Should().Throw<ModelNotFoundException>()
            .Where(e => e.ModelId == "nonexistent");
    }

    [Fact]
    public void TryResolve_ValidAlias_ShouldReturnTrue()
    {
        var success = _registry.TryResolve("default", out var model);

        success.Should().BeTrue();
        model.Should().NotBeNull();
    }

    [Fact]
    public void TryResolve_InvalidAlias_ShouldReturnFalse()
    {
        var success = _registry.TryResolve("nonexistent", out var model);

        success.Should().BeFalse();
        model.Should().BeNull();
    }

    [Fact]
    public void GetAll_ShouldReturnAllBuiltInModels()
    {
        var models = _registry.GetAll().ToList();

        models.Should().HaveCount(5); // RtDetrV2S, RtDetrV2M, RtDetrV2L, RtDetrV2MS, RtDetrV2X
    }

    [Fact]
    public void GetAliases_ShouldReturnAllAliases()
    {
        var aliases = _registry.GetAliases().ToList();

        aliases.Should().Contain(["default", "quality", "fast", "large", "xlarge"]);
    }

    [Fact]
    public void DefaultModels_ShouldAllBeNmsFree()
    {
        var models = _registry.GetAll();

        // All RT-DETR v2 models are NMS-free
        models.Should().OnlyContain(m => m.RequiresNms == false);
        models.Should().OnlyContain(m => m.Architecture == "RT-DETR");
    }

    [Fact]
    public void DefaultModels_ShouldAllHaveApache2License()
    {
        var models = _registry.GetAll();

        models.Should().OnlyContain(m => m.License == "Apache-2.0");
    }
}
