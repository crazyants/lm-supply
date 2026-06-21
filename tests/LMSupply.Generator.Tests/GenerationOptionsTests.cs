using FluentAssertions;
using LMSupply.Generator.Models;

namespace LMSupply.Generator.Tests;

public class GenerationOptionsTests
{
    [Fact]
    public void Default_ReturnsExpectedValues()
    {
        // Act
        var options = GenerationOptions.Default;

        // Assert
        options.MaxTokens.Should().Be(512);
        options.Temperature.Should().Be(0.7f);
        options.TopP.Should().Be(0.9f);
        options.TopK.Should().Be(50);
        options.RepetitionPenalty.Should().Be(1.1f);
    }

    [Fact]
    public void Creative_HasHigherTemperature()
    {
        // Act
        var options = GenerationOptions.Creative;

        // Assert
        options.Temperature.Should().Be(0.9f);
        options.TopP.Should().Be(0.95f);
        options.TopK.Should().Be(100);
    }

    [Fact]
    public void Precise_HasLowerTemperature()
    {
        // Act
        var options = GenerationOptions.Precise;

        // Assert
        options.Temperature.Should().Be(0.1f);
        options.TopP.Should().Be(0.5f);
        options.TopK.Should().Be(10);
    }

    [Fact]
    public void Default_HasExpectedSamplingOptions()
    {
        // Act
        var options = GenerationOptions.Default;

        // Assert - New options from research-05
        options.DoSample.Should().BeTrue();
        options.NumBeams.Should().Be(1);
        options.PastPresentShareBuffer.Should().BeTrue();
        options.MaxNewTokens.Should().BeNull();
    }

    [Fact]
    public void BeamSearch_Configuration()
    {
        // Arrange
        var options = new GenerationOptions
        {
            NumBeams = 4,
            DoSample = false
        };

        // Assert
        options.NumBeams.Should().Be(4);
        options.DoSample.Should().BeFalse();
    }

    [Fact]
    public void MaxNewTokens_CanBeLimited()
    {
        // Arrange
        var options = new GenerationOptions
        {
            MaxTokens = 2048,
            MaxNewTokens = 100
        };

        // Assert
        options.MaxTokens.Should().Be(2048);
        options.MaxNewTokens.Should().Be(100);
    }

    [Fact]
    public void Default_Thinking_IsAuto()
    {
        GenerationOptions.Default.Thinking.Should().Be(ThinkingMode.Auto,
            because: "the default must preserve each model's built-in thinking behavior, not force it on or off");
    }

    [Fact]
    public void Gemma4Preset_Thinking_IsAuto()
    {
        GenerationOptions.Gemma4.Thinking.Should().Be(ThinkingMode.Auto,
            because: "Gemma4 preset controls sampler params only; thinking is an independent per-call-site setting");
    }

    [Fact]
    public void Qwen3_HasExpectedSamplingParameters()
    {
        var opts = GenerationOptions.Qwen3;

        opts.Temperature.Should().BeApproximately(0.6f, 0.0001f);
        opts.TopP.Should().BeApproximately(0.95f, 0.0001f);
        opts.TopK.Should().Be(20);
        opts.MinP.Should().BeApproximately(0.0f, 0.0001f);
        opts.RepetitionPenalty.Should().BeApproximately(1.0f, 0.0001f);
    }
}
