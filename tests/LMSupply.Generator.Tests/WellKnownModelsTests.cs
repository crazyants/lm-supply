using FluentAssertions;

namespace LMSupply.Generator.Tests;

public class WellKnownModelsTests
{
    [Fact]
    public void Generator_Default_IsPhi4Mini()
    {
        // Assert
        WellKnownModels.Generator.Default.Should().Be("microsoft/Phi-4-mini-instruct-onnx");
    }

    [Fact]
    public void Generator_Quality_IsPhi4()
    {
        // Assert
        WellKnownModels.Generator.Quality.Should().Be("microsoft/phi-4-onnx");
    }

    [Fact]
    public void Generator_Fast_IsPhi4Mini()
    {
        // Fast is now Phi4Mini (smallest FC-capable ONNX model)
        WellKnownModels.Generator.Fast.Should().Be(WellKnownModels.Generator.Default);
    }

    [Fact]
    public void Generator_Small_IsSameAsFast()
    {
        // Assert
        WellKnownModels.Generator.Small.Should().Be(WellKnownModels.Generator.Fast);
    }

    [Fact]
    public void GetLicenseTier_DefaultModel_ReturnsMIT()
    {
        // Act
        var tier = WellKnownModels.GetLicenseTier(WellKnownModels.Generator.Default);

        // Assert
        tier.Should().Be(LicenseTier.MIT);
    }

    [Fact]
    public void GetLicenseTier_Fast_ReturnsMIT()
    {
        // Fast is now Phi4Mini (MIT license)
        var tier = WellKnownModels.GetLicenseTier(WellKnownModels.Generator.Fast);

        tier.Should().Be(LicenseTier.MIT);
    }

    [Fact]
    public void HasRestrictions_MITModel_ReturnsFalse()
    {
        // Act
        var hasRestrictions = WellKnownModels.HasRestrictions(WellKnownModels.Generator.Default);

        // Assert
        hasRestrictions.Should().BeFalse();
    }

    [Fact]
    public void HasRestrictions_FastModel_ReturnsFalse()
    {
        // Fast is now Phi4Mini (MIT, no restrictions)
        var hasRestrictions = WellKnownModels.HasRestrictions(WellKnownModels.Generator.Fast);

        hasRestrictions.Should().BeFalse();
    }

    [Fact]
    public void GetUnrestrictedModels_ContainsMITModelsOnly()
    {
        var models = WellKnownModels.GetUnrestrictedModels();

        models.Should().Contain(WellKnownModels.Generator.Default);
        models.Should().Contain(WellKnownModels.Generator.Quality);
        // Fast == Default (Phi4Mini), so it's also in the unrestricted list
        models.Should().Contain(WellKnownModels.Generator.Fast);
    }

    [Fact]
    public void Embedder_Default_IsNotEmpty()
    {
        // Assert
        WellKnownModels.Embedder.Default.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Reranker_Default_IsNotEmpty()
    {
        // Assert
        WellKnownModels.Reranker.Default.Should().NotBeNullOrEmpty();
    }
}
