using FluentAssertions;
using LMSupply.Segmenter.Models;

namespace LMSupply.Segmenter.Tests;

public class SegmenterModelRegistryTests
{
    private readonly SegmenterModelRegistry _registry = SegmenterModelRegistry.Default;

    [Theory]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    [InlineData("Default")]
    public void Resolve_DefaultAlias_ShouldReturnSegFormerB0(string alias)
    {
        var model = _registry.Resolve(alias);

        model.Should().NotBeNull();
        model.Id.Should().Be("optimum/segformer-b0-finetuned-ade-512-512");
        model.AliasName.Should().Be("default");
    }

    [Theory]
    [InlineData("quality", "onnx-community/maskformer-resnet50-ade20k-full")]
    [InlineData("fast", "onnx-community/mediapipe_selfie_segmentation")]
    [InlineData("large", "optimum/segformer-b0-finetuned-ade-512-512")]
    public void Resolve_BuiltInAliases_ShouldReturnCorrectModel(string alias, string expectedId)
    {
        var model = _registry.Resolve(alias);

        model.Should().NotBeNull();
        model.Id.Should().Be(expectedId);
    }

    [Fact]
    public void Resolve_FullModelId_ShouldReturnModel()
    {
        var model = _registry.Resolve("optimum/segformer-b0-finetuned-ade-512-512");

        model.Should().NotBeNull();
        model.DisplayName.Should().Contain("SegFormer-B0");
    }

    [Fact]
    public void Resolve_UnknownHuggingFaceId_ShouldCreateGenericModel()
    {
        var model = _registry.Resolve("some-org/some-segmenter");

        model.Should().NotBeNull();
        model.Id.Should().Be("some-org/some-segmenter");
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

        // SegFormerB0, MediaPipeSelfie, MaskFormerResNet50, SegFormerB0Large (same ID as B0), MobileSAM
        // But SegFormerB0 and SegFormerB0Large share the same ID so only 4 unique by ID
        models.Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void GetAliases_ShouldReturnAllAliases()
    {
        var aliases = _registry.GetAliases().ToList();

        aliases.Should().Contain(["default", "quality", "fast", "large", "interactive"]);
    }

    [Fact]
    public void DefaultModels_ShouldHaveApache2License()
    {
        var models = _registry.GetAll();

        // All ONNX models use Apache-2.0 license
        models.Should().OnlyContain(m => m.License == "Apache-2.0");
    }

    [Fact]
    public void DefaultModels_MobileSAM_ShouldHaveCorrectProperties()
    {
        var model = _registry.Resolve("interactive");

        model.Architecture.Should().Be("MobileSAM");
        model.Id.Should().Be("ChaoningZhang/MobileSAM");
        model.License.Should().Be("Apache-2.0");
        model.IsInteractive.Should().BeTrue();
        model.EncoderFile.Should().Be("mobile_sam_image_encoder.onnx");
        model.DecoderFile.Should().Be("mobile_sam_mask_decoder.onnx");
    }

    [Fact]
    public void DefaultModels_NonInteractiveModels_ShouldNotHaveEncoderDecoder()
    {
        var models = _registry.GetAll()
            .Where(m => !m.IsInteractive);

        models.Should().OnlyContain(m => m.EncoderFile == null);
        models.Should().OnlyContain(m => m.DecoderFile == null);
    }
}
