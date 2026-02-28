using Microsoft.Extensions.AI;
using Moq;
using NanoBot.Providers;
using Xunit;

namespace NanoBot.Providers.Tests;

public class MessageSanitizerTests
{
    [Fact]
    public void SanitizeMessages_Null_ReturnsNull()
    {
        var result = MessageSanitizer.SanitizeMessages(null!);

        Assert.Null(result);
    }

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

    [Fact]
    public void SanitizeMessages_EmptyTextWithoutToolCalls_ReplacedWithPlaceholder()
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "")
        };

        var result = MessageSanitizer.SanitizeMessages(messages);

        Assert.Equal("(empty)", result[0].Text);
    }

    [Fact]
    public void SanitizeMessages_EmptyTextAssistantWithToolCalls_AllowsNull()
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
    public void SanitizeMessages_PreservesFunctionCallsWithEmptyText()
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.Assistant, "")
            {
                Contents = 
                { 
                    new FunctionCallContent("search", "call123", new Dictionary<string, object?> { ["query"] = "test" })
                }
            }
        };

        var result = MessageSanitizer.SanitizeMessages(messages);

        Assert.Single(result[0].Contents.OfType<FunctionCallContent>());
        Assert.NotNull(result[0].Text);
    }

    [Fact]
    public void SanitizeMessages_PreservesMultipleFunctionCalls()
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.Assistant, "I'll help with that")
            {
                Contents = 
                { 
                    new FunctionCallContent("search", "call123", new Dictionary<string, object?> { ["query"] = "test" }),
                    new FunctionCallContent("read_file", "call456", new Dictionary<string, object?> { ["path"] = "file.txt" })
                }
            }
        };

        var result = MessageSanitizer.SanitizeMessages(messages);

        var functionCalls = result[0].Contents.OfType<FunctionCallContent>().ToList();
        Assert.Equal(2, functionCalls.Count);
        Assert.Equal("I'll help with that", result[0].Text);
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
    public void SanitizingChatClient_Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SanitizingChatClient(null!));
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

    [Fact]
    public async Task SanitizingChatClient_GetResponseAsync_SanitizesMessagesBeforeCallingInner()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        IEnumerable<ChatMessage>? capturedMessages = null;
        mockInner.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done")));

        var client = new SanitizingChatClient(mockInner.Object);
        var inputMessages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "Hello") { RawRepresentation = new object() }
        };

        await client.GetResponseAsync(inputMessages, null, CancellationToken.None);

        Assert.NotNull(capturedMessages);
        var list = capturedMessages!.ToList();
        Assert.Single(list);
        Assert.Null(list[0].RawRepresentation);
    }

    [Fact]
    public async Task SanitizingChatClient_GetStreamingResponseAsync_SanitizesMessagesAndStreams()
    {
        var mockInner = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var update = new ChatResponseUpdate(ChatRole.Assistant, "Hi");
        mockInner.Setup(x => x.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(StreamUpdates(update));

        var client = new SanitizingChatClient(mockInner.Object);
        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "Hello") };
        var count = 0;

        await foreach (var u in client.GetStreamingResponseAsync(messages, null, CancellationToken.None))
        {
            count++;
            Assert.Equal(ChatRole.Assistant, u.Role);
            Assert.Equal("Hi", u.Text);
        }

        Assert.Equal(1, count);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamUpdates(ChatResponseUpdate update)
    {
        yield return update;
        await Task.CompletedTask;
    }
}

public class MessageSanitizerContainsThinkTagsTests
{
    [Fact]
    public void ContainsThinkTags_EmptyString_ReturnsFalse()
    {
        Assert.False(MessageSanitizer.ContainsThinkTags(""));
    }

    [Fact]
    public void ContainsThinkTags_Null_ReturnsFalse()
    {
        Assert.False(MessageSanitizer.ContainsThinkTags(null));
    }

    [Fact]
    public void ContainsThinkTags_NoThinkTags_ReturnsFalse()
    {
        Assert.False(MessageSanitizer.ContainsThinkTags("Hello, world!"));
    }

    [Fact]
    public void ContainsThinkTags_WithThinkTags_ReturnsTrue()
    {
        Assert.True(MessageSanitizer.ContainsThinkTags("<think>reasoning</think> Response"));
    }

    [Fact]
    public void ContainsThinkTags_MultiLineThink_ReturnsTrue()
    {
        Assert.True(MessageSanitizer.ContainsThinkTags(@"<think>
line1
line2
</think>
Answer"));
    }

    [Fact]
    public void ContainsThinkTags_CaseInsensitive_ReturnsTrue()
    {
        Assert.True(MessageSanitizer.ContainsThinkTags("<THINK>foo</THINK>"));
    }
}
