using FluentAssertions;
using LMSupply.Generator.Internal;

namespace LMSupply.Generator.Tests;

public class ToolCallTextParserTests
{
    [Fact]
    public void TryParse_NormalText_ReturnsNull()
    {
        var result = ToolCallTextParser.TryParse("Hello, how can I help you today?");

        result.Should().BeNull();
    }

    [Fact]
    public void TryParse_ValidToolCallsJson_ParsesCorrectly()
    {
        var json = """
            {"tool_calls": [{"id": "call_123", "type": "function", "function": {"name": "get_weather", "arguments": "{\"city\": \"Seoul\"}"}}]}
            """;

        var result = ToolCallTextParser.TryParse(json);

        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result![0].Id.Should().Be("call_123");
        result[0].FunctionName.Should().Be("get_weather");
        result[0].Arguments.Should().Contain("Seoul");
    }

    [Fact]
    public void TryParse_DirectFunctionCallJson_ParsesCorrectly()
    {
        var json = """
            {"name": "get_weather", "arguments": {"city": "Seoul"}}
            """;

        var result = ToolCallTextParser.TryParse(json);

        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result![0].FunctionName.Should().Be("get_weather");
        result[0].Arguments.Should().Contain("Seoul");
        result[0].Id.Should().StartWith("call_");
    }

    [Fact]
    public void TryParse_MissingId_GeneratesId()
    {
        var json = """
            {"tool_calls": [{"type": "function", "function": {"name": "search", "arguments": "{\"q\": \"test\"}"}}]}
            """;

        var result = ToolCallTextParser.TryParse(json);

        result.Should().NotBeNull();
        result![0].Id.Should().StartWith("call_");
        result[0].FunctionName.Should().Be("search");
    }

    [Fact]
    public void TryParse_MultipleToolCalls_ParsesAll()
    {
        var json = """
            {"tool_calls": [
                {"id": "call_1", "type": "function", "function": {"name": "get_weather", "arguments": "{\"city\": \"Seoul\"}"}},
                {"id": "call_2", "type": "function", "function": {"name": "get_time", "arguments": "{\"timezone\": \"KST\"}"}}
            ]}
            """;

        var result = ToolCallTextParser.TryParse(json);

        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result![0].FunctionName.Should().Be("get_weather");
        result[1].FunctionName.Should().Be("get_time");
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsNull()
    {
        var result = ToolCallTextParser.TryParse("{invalid json here}}}");

        result.Should().BeNull();
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsNull()
    {
        ToolCallTextParser.TryParse("").Should().BeNull();
    }

    [Fact]
    public void TryParse_Whitespace_ReturnsNull()
    {
        ToolCallTextParser.TryParse("   \n\t  ").Should().BeNull();
    }

    [Fact]
    public void TryParse_Null_ReturnsNull()
    {
        ToolCallTextParser.TryParse(null).Should().BeNull();
    }

    [Fact]
    public void TryParse_JsonWithExtraWhitespace_ParsesCorrectly()
    {
        var json = """

              {
                "tool_calls": [
                  {
                    "id": "call_abc",
                    "type": "function",
                    "function": {
                      "name": "calculate",
                      "arguments": "{\"expression\": \"2+2\"}"
                    }
                  }
                ]
              }

            """;

        var result = ToolCallTextParser.TryParse(json);

        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result![0].Id.Should().Be("call_abc");
        result[0].FunctionName.Should().Be("calculate");
    }

    [Fact]
    public void TryParse_DirectFunctionCall_WithObjectArguments_SerializesAsJson()
    {
        var json = """{"name": "search", "arguments": {"query": "hello", "limit": 10}}""";

        var result = ToolCallTextParser.TryParse(json);

        result.Should().NotBeNull();
        result![0].FunctionName.Should().Be("search");
        result[0].Arguments.Should().Contain("query");
        result[0].Arguments.Should().Contain("10");
    }

    [Fact]
    public void TryParse_JsonWithoutToolCallsOrName_ReturnsNull()
    {
        var json = """{"type": "response", "content": "Hello world"}""";

        var result = ToolCallTextParser.TryParse(json);

        result.Should().BeNull();
    }

    [Fact]
    public void TryParse_EmptyToolCallsArray_ReturnsNull()
    {
        var json = """{"tool_calls": []}""";

        var result = ToolCallTextParser.TryParse(json);

        result.Should().BeNull();
    }

    [Fact]
    public void TryParse_ToolCallWithoutFunction_SkipsIt()
    {
        var json = """{"tool_calls": [{"id": "call_1", "type": "function"}]}""";

        var result = ToolCallTextParser.TryParse(json);

        result.Should().BeNull();
    }
}
