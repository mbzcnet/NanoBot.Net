using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent.Context;
using NanoBot.Core.Workspace;
using Xunit;

namespace NanoBot.Agent.Tests.Context;

public class ChatHistoryProviderTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IWorkspaceManager> _workspaceMock;
    private readonly Mock<ILogger<FileBackedChatHistoryProvider>> _loggerMock;

    public ChatHistoryProviderTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_history_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        _workspaceMock = new Mock<IWorkspaceManager>();
        _workspaceMock.Setup(w => w.GetHistoryFile())
            .Returns(Path.Combine(_testDirectory, "history.log"));
        _loggerMock = new Mock<ILogger<FileBackedChatHistoryProvider>>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ProvideChatHistoryAsync_ReturnsEmptyWhenNoSession()
    {
        var provider = new FileBackedChatHistoryProvider(_workspaceMock.Object, 100, _loggerMock.Object);
        var agentMock = new Mock<AIAgent>();
        var requestMessage = new ChatMessage(ChatRole.User, "test");
        var context = new ChatHistoryProvider.InvokingContext(
            agentMock.Object,
            null,
            new[] { requestMessage });

        var result = await provider.InvokingAsync(context);

        // InvokingAsync returns history + request messages, so should only have the request message
        var resultList = result.ToList();
        Assert.Single(resultList);
        Assert.Equal("test", resultList[0].Text);
    }

    [Fact]
    public async Task ProvideChatHistoryAsync_ReturnsEmptyWhenNoHistoryInSession()
    {
        var provider = new FileBackedChatHistoryProvider(_workspaceMock.Object, 100, _loggerMock.Object);
        var agent = CreateTestAgent();
        var session = await agent.CreateSessionAsync();
        var requestMessage = new ChatMessage(ChatRole.User, "test");
        var context = new ChatHistoryProvider.InvokingContext(
            agent,
            session,
            new[] { requestMessage });

        var result = await provider.InvokingAsync(context);

        // InvokingAsync returns history + request messages, so should only have the request message
        var resultList = result.ToList();
        Assert.Single(resultList);
        Assert.Equal("test", resultList[0].Text);
    }

    [Fact]
    public async Task StoreChatHistoryAsync_StoresMessagesInSession()
    {
        var provider = new FileBackedChatHistoryProvider(_workspaceMock.Object, 100, _loggerMock.Object);
        var agent = CreateTestAgent();
        var session = await agent.CreateSessionAsync();

        var requestMessages = new[] { new ChatMessage(ChatRole.User, "Hello") };
        var responseMessages = new[] { new ChatMessage(ChatRole.Assistant, "Hi there") };

        var context = new ChatHistoryProvider.InvokedContext(
            agent,
            session,
            requestMessages,
            responseMessages);

        await provider.InvokedAsync(context);

        // Verify messages are stored in session
        var storedMessages = session.StateBag.GetValue<List<ChatMessage>>(provider.StateKey);
        Assert.NotNull(storedMessages);
        Assert.Equal(2, storedMessages.Count);
        Assert.Equal("Hello", storedMessages[0].Text);
        Assert.Equal("Hi there", storedMessages[1].Text);
    }

    [Fact]
    public async Task ChatHistory_PreservesAcrossMultipleInvocations()
    {
        var provider = new FileBackedChatHistoryProvider(_workspaceMock.Object, 100, _loggerMock.Object);
        var agent = CreateTestAgent();
        var session = await agent.CreateSessionAsync();

        // First invocation
        var request1 = new[] { new ChatMessage(ChatRole.User, "First message") };
        var response1 = new[] { new ChatMessage(ChatRole.Assistant, "First response") };
        var invokedContext1 = new ChatHistoryProvider.InvokedContext(
            agent, session, request1, response1);
        await provider.InvokedAsync(invokedContext1);

        // Second invocation - should retrieve history
        var request2 = new[] { new ChatMessage(ChatRole.User, "Second message") };
        var invokingContext2 = new ChatHistoryProvider.InvokingContext(
            agent, session, request2);
        var allMessages = await provider.InvokingAsync(invokingContext2);

        // InvokingAsync returns history + request messages
        var allMessagesList = allMessages.ToList();
        Assert.Equal(3, allMessagesList.Count); // 2 history + 1 request
        Assert.Equal("First message", allMessagesList[0].Text);
        Assert.Equal("First response", allMessagesList[1].Text);
        Assert.Equal("Second message", allMessagesList[2].Text);

        // Store second invocation
        var response2 = new[] { new ChatMessage(ChatRole.Assistant, "Second response") };
        var invokedContext2 = new ChatHistoryProvider.InvokedContext(
            agent, session, request2, response2);
        await provider.InvokedAsync(invokedContext2);

        // Third invocation - should have all history
        var request3 = new[] { new ChatMessage(ChatRole.User, "Third message") };
        var invokingContext3 = new ChatHistoryProvider.InvokingContext(
            agent, session, request3);
        var fullHistory = await provider.InvokingAsync(invokingContext3);

        var fullHistoryList = fullHistory.ToList();
        Assert.Equal(5, fullHistoryList.Count); // 4 history + 1 request
        Assert.Equal("First message", fullHistoryList[0].Text);
        Assert.Equal("First response", fullHistoryList[1].Text);
        Assert.Equal("Second message", fullHistoryList[2].Text);
        Assert.Equal("Second response", fullHistoryList[3].Text);
        Assert.Equal("Third message", fullHistoryList[4].Text);
    }

    [Fact]
    public async Task StoreChatHistoryAsync_TrimsOldMessagesWhenExceedingLimit()
    {
        var provider = new FileBackedChatHistoryProvider(_workspaceMock.Object, maxHistoryEntries: 4, _loggerMock.Object);
        var agent = CreateTestAgent();
        var session = await agent.CreateSessionAsync();

        // Add 6 messages (3 exchanges)
        for (int i = 1; i <= 3; i++)
        {
            var request = new[] { new ChatMessage(ChatRole.User, $"Message {i}") };
            var response = new[] { new ChatMessage(ChatRole.Assistant, $"Response {i}") };
            var context = new ChatHistoryProvider.InvokedContext(
                agent, session, request, response);
            await provider.InvokedAsync(context);
        }

        // Verify only last 4 messages are kept
        var storedMessages = session.StateBag.GetValue<List<ChatMessage>>(provider.StateKey);
        Assert.NotNull(storedMessages);
        Assert.Equal(4, storedMessages.Count);
        Assert.Equal("Message 2", storedMessages[0].Text);
        Assert.Equal("Response 2", storedMessages[1].Text);
        Assert.Equal("Message 3", storedMessages[2].Text);
        Assert.Equal("Response 3", storedMessages[3].Text);
    }

    [Fact]
    public async Task StoreChatHistoryAsync_AppendsToHistoryFile()
    {
        var provider = new FileBackedChatHistoryProvider(_workspaceMock.Object, 100, _loggerMock.Object);
        var agent = CreateTestAgent();
        var session = await agent.CreateSessionAsync();

        var requestMessages = new[] { new ChatMessage(ChatRole.User, "Test message") };
        var responseMessages = new[] { new ChatMessage(ChatRole.Assistant, "Test response") };

        var context = new ChatHistoryProvider.InvokedContext(
            agent, session, requestMessages, responseMessages);

        await provider.InvokedAsync(context);

        var historyFile = _workspaceMock.Object.GetHistoryFile();
        Assert.True(File.Exists(historyFile));

        var content = await File.ReadAllTextAsync(historyFile);
        Assert.Contains("user: Test message", content);
        Assert.Contains("assistant: Test response", content);
    }

    private static ChatClientAgent CreateTestAgent()
    {
        var chatClientMock = new Mock<IChatClient>();
        var metadata = new ChatClientMetadata("test");
        chatClientMock.Setup(c => c.GetService(typeof(ChatClientMetadata), null))
            .Returns(metadata);

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test response"));
        chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var options = new ChatClientAgentOptions
        {
            Name = "TestAgent",
            Description = "Test Description"
        };

        return new ChatClientAgent(chatClientMock.Object, options);
    }
}
