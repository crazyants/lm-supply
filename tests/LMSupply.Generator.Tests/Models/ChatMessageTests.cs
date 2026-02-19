using FluentAssertions;
using LMSupply.Generator.Models;

namespace LMSupply.Generator.Tests;

public class ChatMessageTests
{
    [Fact]
    public void System_ShouldCreateSystemMessage()
    {
        var msg = ChatMessage.System("You are a helpful assistant.");

        msg.Role.Should().Be(ChatRole.System);
        msg.Content.Should().Be("You are a helpful assistant.");
    }

    [Fact]
    public void User_ShouldCreateUserMessage()
    {
        var msg = ChatMessage.User("Hello!");

        msg.Role.Should().Be(ChatRole.User);
        msg.Content.Should().Be("Hello!");
    }

    [Fact]
    public void Assistant_ShouldCreateAssistantMessage()
    {
        var msg = ChatMessage.Assistant("Hi there!");

        msg.Role.Should().Be(ChatRole.Assistant);
        msg.Content.Should().Be("Hi there!");
    }

    [Fact]
    public void StructEquality_SameValues_ShouldBeEqual()
    {
        var a = ChatMessage.User("Hello");
        var b = ChatMessage.User("Hello");

        a.Should().Be(b);
    }

    [Fact]
    public void StructEquality_DifferentContent_ShouldNotBeEqual()
    {
        var a = ChatMessage.User("Hello");
        var b = ChatMessage.User("World");

        a.Should().NotBe(b);
    }

    [Fact]
    public void StructEquality_DifferentRole_ShouldNotBeEqual()
    {
        var a = ChatMessage.User("Hello");
        var b = ChatMessage.Assistant("Hello");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Constructor_ShouldInitializeFromParameters()
    {
        var msg = new ChatMessage(ChatRole.System, "Test content");

        msg.Role.Should().Be(ChatRole.System);
        msg.Content.Should().Be("Test content");
    }

    [Fact]
    public void DefaultStruct_ShouldHaveDefaultValues()
    {
        var msg = default(ChatMessage);

        msg.Role.Should().Be(ChatRole.System); // enum default = 0 = System
        msg.Content.Should().BeNull();
    }
}

public class ChatRoleEnumTests
{
    [Fact]
    public void ShouldHaveThreeValues()
    {
        Enum.GetValues<ChatRole>().Should().HaveCount(3);
    }

    [Theory]
    [InlineData(ChatRole.System, 0)]
    [InlineData(ChatRole.User, 1)]
    [InlineData(ChatRole.Assistant, 2)]
    public void ShouldHaveExpectedIntValues(ChatRole role, int expected)
    {
        ((int)role).Should().Be(expected);
    }
}

public class ReasoningResultTests
{
    [Fact]
    public void ShouldInitialize_WithParameters()
    {
        var result = new ReasoningResult("The answer is 42.", "<think>Let me calculate...</think>");

        result.Response.Should().Be("The answer is 42.");
        result.Reasoning.Should().Be("<think>Let me calculate...</think>");
    }

    [Fact]
    public void StructEquality_SameValues_ShouldBeEqual()
    {
        var a = new ReasoningResult("answer", "reasoning");
        var b = new ReasoningResult("answer", "reasoning");

        a.Should().Be(b);
    }

    [Fact]
    public void StructEquality_DifferentValues_ShouldNotBeEqual()
    {
        var a = new ReasoningResult("answer1", "reasoning");
        var b = new ReasoningResult("answer2", "reasoning");

        a.Should().NotBe(b);
    }

    [Fact]
    public void DefaultStruct_ShouldHaveNullValues()
    {
        var result = default(ReasoningResult);

        result.Response.Should().BeNull();
        result.Reasoning.Should().BeNull();
    }

    [Fact]
    public void Deconstruct_ShouldWork()
    {
        var result = new ReasoningResult("response", "thought");
        var (response, reasoning) = result;

        response.Should().Be("response");
        reasoning.Should().Be("thought");
    }
}

public class ModelFormatEnumTests
{
    [Fact]
    public void ShouldHaveThreeValues()
    {
        Enum.GetValues<ModelFormat>().Should().HaveCount(3);
    }

    [Theory]
    [InlineData(ModelFormat.Onnx, 0)]
    [InlineData(ModelFormat.Gguf, 1)]
    [InlineData(ModelFormat.Unknown, 2)]
    public void ShouldHaveExpectedIntValues(ModelFormat format, int expected)
    {
        ((int)format).Should().Be(expected);
    }
}

public class GeneratorBackendTypeEnumTests
{
    [Fact]
    public void ShouldHaveTwoValues()
    {
        Enum.GetValues<GeneratorBackendType>().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(GeneratorBackendType.OnnxGenAI, 0)]
    [InlineData(GeneratorBackendType.LlamaCpp, 1)]
    public void ShouldHaveExpectedIntValues(GeneratorBackendType type, int expected)
    {
        ((int)type).Should().Be(expected);
    }
}
