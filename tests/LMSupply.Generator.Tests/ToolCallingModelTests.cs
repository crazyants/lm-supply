using FluentAssertions;
using LMSupply.Generator.Models;

namespace LMSupply.Generator.Tests;

public class ToolCallingModelTests
{
    [Fact]
    public void ChatToolCall_Creation_SetsProperties()
    {
        var toolCall = new ChatToolCall
        {
            Id = "call_123",
            FunctionName = "get_weather",
            Arguments = """{"location": "Seoul"}"""
        };

        toolCall.Id.Should().Be("call_123");
        toolCall.FunctionName.Should().Be("get_weather");
        toolCall.Arguments.Should().Contain("Seoul");
    }

    [Fact]
    public void ChatToolDefinition_Creation_SetsProperties()
    {
        var tool = new ChatToolDefinition
        {
            Function = new ChatFunctionDefinition
            {
                Name = "get_weather",
                Description = "Get weather for a location",
                Parameters = """{"type":"object","properties":{"location":{"type":"string"}}}"""
            }
        };

        tool.Type.Should().Be("function");
        tool.Function.Name.Should().Be("get_weather");
        tool.Function.Description.Should().NotBeNull();
    }

    [Fact]
    public void ChatMessage_AssistantToolCalls_CreatesCorrectMessage()
    {
        var toolCalls = new[]
        {
            new ChatToolCall { Id = "call_1", FunctionName = "search", Arguments = "{}" }
        };

        var msg = ChatMessage.AssistantToolCalls(toolCalls);

        msg.Role.Should().Be(ChatRole.Assistant);
        msg.ToolCalls.Should().HaveCount(1);
        msg.ToolCalls![0].FunctionName.Should().Be("search");
    }

    [Fact]
    public void ChatMessage_ToolResult_CreatesCorrectMessage()
    {
        var msg = ChatMessage.ToolResult("call_1", "Result data");

        msg.Role.Should().Be(ChatRole.Tool);
        msg.Content.Should().Be("Result data");
        msg.ToolCallId.Should().Be("call_1");
    }

    [Fact]
    public void ChatRole_Tool_HasValue3()
    {
        ((int)ChatRole.Tool).Should().Be(3);
    }

    [Fact]
    public void ChatCompletionResult_HasToolCalls_ReturnsTrueWhenPresent()
    {
        var result = new ChatCompletionResult
        {
            ToolCalls = new[] { new ChatToolCall { Id = "1", FunctionName = "f", Arguments = "{}" } }
        };

        result.HasToolCalls.Should().BeTrue();
    }

    [Fact]
    public void ChatCompletionResult_HasToolCalls_ReturnsFalseWhenEmpty()
    {
        var result = new ChatCompletionResult { Content = "Hello" };

        result.HasToolCalls.Should().BeFalse();
    }

    [Fact]
    public void GenerationOptions_Tools_DefaultIsNull()
    {
        var options = new GenerationOptions();
        options.Tools.Should().BeNull();
    }
}
