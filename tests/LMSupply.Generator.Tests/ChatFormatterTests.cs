using FluentAssertions;
using LMSupply.Generator.ChatFormatters;
using LMSupply.Generator.Models;

namespace LMSupply.Generator.Tests;

public class ChatFormatterTests
{
    [Fact]
    public void Phi3ChatFormatter_FormatPrompt_FormatsCorrectly()
    {
        // Arrange
        var formatter = new Phi3ChatFormatter();
        var messages = new[]
        {
            ChatMessage.System("You are a helpful assistant."),
            ChatMessage.User("Hello!")
        };

        // Act
        var result = formatter.FormatPrompt(messages);

        // Assert
        result.Should().Contain("<|system|>");
        result.Should().Contain("You are a helpful assistant.");
        result.Should().Contain("<|end|>");
        result.Should().Contain("<|user|>");
        result.Should().Contain("Hello!");
        result.Should().EndWith("<|assistant|>\n");
    }

    [Fact]
    public void Phi3ChatFormatter_GetStopSequences_ReturnsExpectedSequences()
    {
        // Arrange
        var formatter = new Phi3ChatFormatter();

        // Act
        var stopSequences = formatter.GetStopSequences();

        // Assert
        stopSequences.Should().Contain("<|end|>");
        stopSequences.Should().Contain("<|user|>");
    }

    [Fact]
    public void Llama3ChatFormatter_FormatPrompt_FormatsCorrectly()
    {
        // Arrange
        var formatter = new Llama3ChatFormatter();
        var messages = new[]
        {
            ChatMessage.System("You are a helpful assistant."),
            ChatMessage.User("Hello!")
        };

        // Act
        var result = formatter.FormatPrompt(messages);

        // Assert
        result.Should().Contain("<|begin_of_text|>");
        result.Should().Contain("<|start_header_id|>system<|end_header_id|>");
        result.Should().Contain("You are a helpful assistant.");
        result.Should().Contain("<|eot_id|>");
        result.Should().Contain("<|start_header_id|>user<|end_header_id|>");
        result.Should().EndWith("<|start_header_id|>assistant<|end_header_id|>\n\n");
    }

    [Fact]
    public void ChatMLFormatter_FormatPrompt_FormatsCorrectly()
    {
        // Arrange
        var formatter = new ChatMLFormatter();
        var messages = new[]
        {
            ChatMessage.System("You are a helpful assistant."),
            ChatMessage.User("Hello!")
        };

        // Act
        var result = formatter.FormatPrompt(messages);

        // Assert
        result.Should().Contain("<|im_start|>system");
        result.Should().Contain("You are a helpful assistant.");
        result.Should().Contain("<|im_end|>");
        result.Should().Contain("<|im_start|>user");
        result.Should().EndWith("<|im_start|>assistant\n");
    }

    [Theory]
    [InlineData("phi-3-mini", "phi3")]
    [InlineData("Phi-3.5-mini-instruct", "phi3")]
    [InlineData("llama-3-8b", "llama3")]
    [InlineData("Llama-3.2-1B-Instruct", "llama3")]
    [InlineData("qwen2.5-7b", "chatml")]
    [InlineData("unknown-model", "phi3")] // Default
    public void ChatFormatterFactory_Create_ReturnsCorrectFormatter(string modelName, string expectedFormat)
    {
        // Act
        var formatter = ChatFormatterFactory.Create(modelName);

        // Assert
        formatter.FormatName.Should().Be(expectedFormat);
    }

    [Fact]
    public void GemmaChatFormatter_FormatPrompt_FormatsCorrectly()
    {
        var formatter = new GemmaChatFormatter();
        var messages = new[]
        {
            ChatMessage.User("Hello!"),
            ChatMessage.Assistant("Hi there!"),
            ChatMessage.User("How are you?")
        };

        var result = formatter.FormatPrompt(messages);

        result.Should().Contain("<start_of_turn>user");
        result.Should().Contain("<start_of_turn>model");
        result.Should().Contain("<end_of_turn>");
        result.Should().Contain("Hello!");
        result.Should().EndWith("<start_of_turn>model\n");
    }

    [Fact]
    public void GemmaChatFormatter_GetStopSequences_ReturnsExpected()
    {
        var formatter = new GemmaChatFormatter();

        var stops = formatter.GetStopSequences();

        stops.Should().Contain("<end_of_turn>");
        stops.Should().Contain("<start_of_turn>");
    }

    [Fact]
    public void ExaoneChatFormatter_FormatPrompt_FormatsCorrectly()
    {
        var formatter = new ExaoneChatFormatter();
        var messages = new[]
        {
            ChatMessage.System("You are helpful."),
            ChatMessage.User("Hello!")
        };

        var result = formatter.FormatPrompt(messages);

        result.Should().Contain("[|system|]You are helpful.[|endofturn|]");
        result.Should().Contain("[|user|]Hello![|endofturn|]");
        result.Should().EndWith("[|assistant|]");
    }

    [Fact]
    public void ExaoneChatFormatter_GetStopSequences_ReturnsExpected()
    {
        var formatter = new ExaoneChatFormatter();

        var stops = formatter.GetStopSequences();

        stops.Should().Contain("[|endofturn|]");
        stops.Should().Contain("[|user|]");
    }

    [Fact]
    public void DeepSeekChatFormatter_FormatPrompt_FormatsCorrectly()
    {
        var formatter = new DeepSeekChatFormatter();
        var messages = new[]
        {
            ChatMessage.User("What is 2+2?")
        };

        var result = formatter.FormatPrompt(messages);

        result.Should().Contain("<|user|>");
        result.Should().Contain("What is 2+2?");
        result.Should().EndWith("<|assistant|>\n");
    }

    [Fact]
    public void MistralChatFormatter_FormatPrompt_FormatsCorrectly()
    {
        var formatter = new MistralChatFormatter();
        var messages = new[]
        {
            ChatMessage.System("Be helpful."),
            ChatMessage.User("Hello!"),
            ChatMessage.Assistant("Hi!"),
            ChatMessage.User("How are you?")
        };

        var result = formatter.FormatPrompt(messages);

        result.Should().StartWith("<s>");
        result.Should().Contain("[INST]");
        result.Should().Contain("[/INST]");
        result.Should().Contain("Be helpful.");
        result.Should().Contain("Hello!");
    }

    [Fact]
    public void MistralChatFormatter_GetStopSequences_ReturnsExpected()
    {
        var formatter = new MistralChatFormatter();

        var stops = formatter.GetStopSequences();

        stops.Should().Contain("</s>");
        stops.Should().Contain("[INST]");
    }

    [Fact]
    public void Gemma4ChatFormatter_FormatPrompt_SupportsNativeSystemRole()
    {
        var formatter = new Gemma4ChatFormatter();
        var messages = new[]
        {
            ChatMessage.System("You are a helpful assistant."),
            ChatMessage.User("Hello!"),
            ChatMessage.Assistant("Hi there!"),
            ChatMessage.User("How are you?")
        };

        var result = formatter.FormatPrompt(messages);

        // Gemma 4 supports native system role (unlike Gemma 2 which mapped system→user)
        result.Should().Contain("<start_of_turn>system\nYou are a helpful assistant.<end_of_turn>");
        result.Should().Contain("<start_of_turn>user\nHello!<end_of_turn>");
        result.Should().Contain("<start_of_turn>model\nHi there!<end_of_turn>");
        result.Should().EndWith("<start_of_turn>model\n");
    }

    [Fact]
    public void Gemma4ChatFormatter_FormatName_IsGemma4()
    {
        var formatter = new Gemma4ChatFormatter();
        formatter.FormatName.Should().Be("gemma4");
    }

    [Fact]
    public void Gemma4ChatFormatter_GetStopSequences_ReturnsExpected()
    {
        var formatter = new Gemma4ChatFormatter();
        var stops = formatter.GetStopSequences();

        stops.Should().Contain("<end_of_turn>");
        stops.Should().Contain("<start_of_turn>");
    }

    [Fact]
    public void GemmaChatFormatter_SystemRole_MapsToUser()
    {
        // Verify Gemma 2 formatter still maps system→user (backward compat)
        var formatter = new GemmaChatFormatter();
        var messages = new[]
        {
            ChatMessage.System("Be helpful."),
            ChatMessage.User("Hello!")
        };

        var result = formatter.FormatPrompt(messages);

        // Gemma 2: system is mapped to user
        result.Should().Contain("<start_of_turn>user\nBe helpful.<end_of_turn>");
        result.Should().NotContain("<start_of_turn>system");
    }

    [Theory]
    [InlineData("gemma-4-E4B-it", "gemma4")]
    [InlineData("gemma4-instruct", "gemma4")]
    [InlineData("gemma-2b-instruct", "gemma")]
    public void ChatFormatterFactory_Create_DistinguishesGemmaVersions(string modelName, string expectedFormat)
    {
        var formatter = ChatFormatterFactory.Create(modelName);
        formatter.FormatName.Should().Be(expectedFormat);
    }

    [Theory]
    [InlineData("gemma")]
    [InlineData("gemma4")]
    [InlineData("exaone")]
    [InlineData("deepseek")]
    [InlineData("mistral")]
    [InlineData("mixtral")]
    [InlineData("llama3")]
    [InlineData("chatml")]
    [InlineData("phi3")]
    public void ChatFormatterFactory_CreateByFormat_CreatesFormatter(string format)
    {
        var formatter = ChatFormatterFactory.CreateByFormat(format);

        formatter.Should().NotBeNull();
        formatter.FormatName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ChatFormatterFactory_CreateByFormat_UnknownFormat_Throws()
    {
        var act = () => ChatFormatterFactory.CreateByFormat("unknown_format");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown chat format*");
    }
}
