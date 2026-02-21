using Microsoft.Extensions.AI;
using Moq;
using NanoBot.Providers;
using Xunit;

namespace NanoBot.Providers.Tests;

public class MessageSanitizerTests
{
    [Fact]
    public void SanitizeMessages_EmptyList_ReturnsEmptyList()
    {
        var messages = new List<ChatMessage>();

        var result = MessageSanitizer.SanitizeMessages(messages);

        Assert.Empty(result);
    }

    [Fact]
    public void SanitizeMessages_RemovesRawRepresentation()
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "Hello")
            {
                RawRepresentation = new object()
            }
        };

        var result = MessageSanitizer.SanitizeMessages(messages);

        Assert.Null(result[0].RawRepresentation);
    }

    [Fact]
    public void SanitizeMessages_AssistantWithToolCalls_HasEmptyContent()
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.Assistant, "")
            {
                Contents = { new FunctionCallContent("test", "call123", new Dictionary<string, object?>()) }
            }
        };

        var result = MessageSanitizer.SanitizeMessages(messages);

        Assert.NotNull(result[0].Text);
    }

    [Fact]
    public void SanitizeMessages_PreservesFunctionCallContent()
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.Assistant, "I'll search for that")
            {
                Contents = { new FunctionCallContent("search", "call123", new Dictionary<string, object?> { ["query"] = "test" }) }
            }
        };

        var result = MessageSanitizer.SanitizeMessages(messages);

        var hasFunctionCall = result[0].Contents.Any(c => c is FunctionCallContent);
        Assert.True(hasFunctionCall);
    }

    [Fact]
    public void SanitizeMessages_PreservesFunctionResultContent()
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.Tool, "result content")
            {
                Contents = { new FunctionResultContent("search", "call123") }
            }
        };

        var result = MessageSanitizer.SanitizeMessages(messages);

        var hasFunctionResult = result[0].Contents.Any(c => c is FunctionResultContent);
        Assert.True(hasFunctionResult);
    }

    [Fact]
    public void SanitizeMessages_PreservesUserMessageContent()
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "Hello, world!")
        };

        var result = MessageSanitizer.SanitizeMessages(messages);

        Assert.Equal("Hello, world!", result[0].Text);
    }

    [Fact]
    public void SanitizeMessages_PreservesSystemMessageContent()
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, "You are a helpful assistant.")
        };

        var result = MessageSanitizer.SanitizeMessages(messages);

        Assert.Equal("You are a helpful assistant.", result[0].Text);
    }
}

public class MessageSanitizerStripThinkTagsTests
{
    [Fact]
    public void StripThinkTags_EmptyString_ReturnsEmptyString()
    {
        var result = MessageSanitizer.StripThinkTags("");

        Assert.Equal("", result);
    }

    [Fact]
    public void StripThinkTags_Null_ReturnsEmptyString()
    {
        var result = MessageSanitizer.StripThinkTags(null);

        Assert.Equal("", result);
    }

    [Fact]
    public void StripThinkTags_NoThinkTags_ReturnsOriginal()
    {
        var input = "This is a normal response.";

        var result = MessageSanitizer.StripThinkTags(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void StripThinkTags_RemovesThinkTags()
    {
        var input = "<think>This is reasoning.</think> This is the actual response.";

        var result = MessageSanitizer.StripThinkTags(input);

        Assert.Equal("This is the actual response.", result.Trim());
    }

    [Fact]
    public void StripThinkTags_RemovesMultiLineThinkTags()
    {
        var input = @"<think>
This is a multi-line
reasoning block.
</think>
Final response here.";

        var result = MessageSanitizer.StripThinkTags(input);

        Assert.Equal("Final response here.", result.Trim());
    }

    [Fact]
    public void StripThinkTags_CaseInsensitive()
    {
        var input = "<THINK>Reasoning</THINK> Response";

        var result = MessageSanitizer.StripThinkTags(input);

        Assert.Equal("Response", result.Trim());
    }

    [Fact]
    public void StripThinkTags_MultipleThinkBlocks_RemovesAll()
    {
        var input = "<think>First</think> Middle <think>Second</think> End";

        var result = MessageSanitizer.StripThinkTags(input);

        Assert.Equal("Middle  End", result.Trim());
    }
}

public class SanitizingChatClientTests
{
    [Fact]
    public void SanitizingChatClient_ImplementsIChatClient()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var client = new SanitizingChatClient(mockInner.Object);

        Assert.IsAssignableFrom<Microsoft.Extensions.AI.IChatClient>(client);
    }

    [Fact]
    public void SanitizingChatClient_DisposesInnerClient()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var disposableInner = mockInner.As<IDisposable>();
        var client = new SanitizingChatClient(mockInner.Object);

        client.Dispose();

        disposableInner.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public void SanitizingChatClient_DoesNotDisposeTwice()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var disposableInner = mockInner.As<IDisposable>();
        var client = new SanitizingChatClient(mockInner.Object);

        client.Dispose();
        client.Dispose();

        disposableInner.Verify(x => x.Dispose(), Times.Once);
    }
}
