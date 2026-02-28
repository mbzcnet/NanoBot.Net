using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Moq;
using NanoBot.Core.Skills;
using NanoBot.Core.Workspace;
using Xunit;

namespace NanoBot.Agent.Tests.Integration;

/// <summary>
/// Integration tests to verify that agent messages are correctly passed to LLM with history
/// </summary>
public class MessageHistoryIntegrationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IWorkspaceManager> _workspaceMock;
    private readonly Mock<ISkillsLoader> _skillsLoaderMock;

    public MessageHistoryIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_integration_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        _workspaceMock = new Mock<IWorkspaceManager>();
        _workspaceMock.Setup(w => w.GetWorkspacePath()).Returns(_testDirectory);
        _workspaceMock.Setup(w => w.GetHistoryFile()).Returns(Path.Combine(_testDirectory, "history.log"));
        _workspaceMock.Setup(w => w.GetAgentsFile()).Returns(Path.Combine(_testDirectory, "AGENTS.md"));
        _workspaceMock.Setup(w => w.GetSoulFile()).Returns(Path.Combine(_testDirectory, "SOUL.md"));
        _workspaceMock.Setup(w => w.GetUserFile()).Returns(Path.Combine(_testDirectory, "USER.md"));
        _workspaceMock.Setup(w => w.GetToolsFile()).Returns(Path.Combine(_testDirectory, "TOOLS.md"));
        _workspaceMock.Setup(w => w.GetMemoryFile()).Returns(Path.Combine(_testDirectory, "MEMORY.md"));

        _skillsLoaderMock = new Mock<ISkillsLoader>();
        _skillsLoaderMock.Setup(s => s.GetLoadedSkills())
            .Returns(new List<Skill>());
        _skillsLoaderMock.Setup(s => s.GetAlwaysSkills())
            .Returns(new List<string>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Agent_PassesHistoryMessagesToLLM()
    {
        // Arrange
        var receivedMessages = new List<List<ChatMessage>>();
        var chatClientMock = new Mock<IChatClient>();
        var metadata = new ChatClientMetadata("test");
        chatClientMock.Setup(c => c.GetService(typeof(ChatClientMetadata), null))
            .Returns(metadata);

        chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((msgs, opts, ct) =>
            {
                var msgList = msgs.ToList();
                receivedMessages.Add(msgList);
                
                // Debug output
                Console.WriteLine($"\n=== Call #{receivedMessages.Count} ===");
                foreach (var msg in msgList)
                {
                    var preview = msg.Text?.Length > 100 ? msg.Text[..100] + "..." : msg.Text;
                    Console.WriteLine($"  {msg.Role}: {preview}");
                }
            })
            .ReturnsAsync((IEnumerable<ChatMessage> msgs, ChatOptions opts, CancellationToken ct) =>
            {
                var count = receivedMessages.Count;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Response {count}"));
            });

        var agent = NanoBotAgentFactory.Create(
            chatClientMock.Object,
            _workspaceMock.Object,
            _skillsLoaderMock.Object);

        var session = await agent.CreateSessionAsync();

        // Act - First message
        await agent.RunAsync("First question", session);

        // Assert - First call should only have the user message (plus system instructions)
        Assert.Single(receivedMessages);
        var firstCallMessages = receivedMessages[0].Where(m => m.Role == ChatRole.User).ToList();
        Assert.Single(firstCallMessages);
        Assert.Contains("First question", firstCallMessages[0].Text);

        // Act - Second message
        await agent.RunAsync("Second question", session);

        // Assert - Second call should have history + new message
        Assert.Equal(2, receivedMessages.Count);
        var secondCallMessages = receivedMessages[1].Where(m => m.Role != ChatRole.System).ToList();
        
        // Should have: First user message, First assistant response, Second user message
        Assert.True(secondCallMessages.Count >= 3, $"Expected at least 3 messages, got {secondCallMessages.Count}");
        
        // Verify the messages are in order
        var userMessages = secondCallMessages.Where(m => m.Role == ChatRole.User).ToList();
        var assistantMessages = secondCallMessages.Where(m => m.Role == ChatRole.Assistant).ToList();
        
        Assert.True(userMessages.Count >= 2, "Should have at least 2 user messages");
        Assert.Single(assistantMessages);
        
        // Check that history is present
        Assert.Contains(userMessages, m => m.Text != null && m.Text.Contains("First question"));
        Assert.Contains(userMessages, m => m.Text != null && m.Text.Contains("Second question"));
        Assert.Contains(assistantMessages[0].Text, "Response 1");
    }

    [Fact]
    public async Task Agent_PreservesHistoryAcrossMultipleExchanges()
    {
        // Arrange
        var receivedMessages = new List<List<ChatMessage>>();
        var chatClientMock = new Mock<IChatClient>();
        var metadata = new ChatClientMetadata("test");
        chatClientMock.Setup(c => c.GetService(typeof(ChatClientMetadata), null))
            .Returns(metadata);

        chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((msgs, opts, ct) =>
            {
                receivedMessages.Add(msgs.ToList());
            })
            .ReturnsAsync((IEnumerable<ChatMessage> msgs, ChatOptions opts, CancellationToken ct) =>
            {
                var count = receivedMessages.Count;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Answer {count}"));
            });

        var agent = NanoBotAgentFactory.Create(
            chatClientMock.Object,
            _workspaceMock.Object,
            _skillsLoaderMock.Object);

        var session = await agent.CreateSessionAsync();

        // Act - Three exchanges
        await agent.RunAsync("Question 1", session);
        await agent.RunAsync("Question 2", session);
        await agent.RunAsync("Question 3", session);

        // Assert - Third call should have all previous history
        Assert.Equal(3, receivedMessages.Count);
        var thirdCallMessages = receivedMessages[2].Where(m => m.Role != ChatRole.System).ToList();
        
        var userMessages = thirdCallMessages.Where(m => m.Role == ChatRole.User).ToList();
        var assistantMessages = thirdCallMessages.Where(m => m.Role == ChatRole.Assistant).ToList();
        
        // Should have 3 user messages and 2 assistant responses
        Assert.True(userMessages.Count >= 3, $"Expected at least 3 user messages, got {userMessages.Count}");
        Assert.True(assistantMessages.Count >= 2, $"Expected at least 2 assistant messages, got {assistantMessages.Count}");
        
        // Verify all questions are present
        Assert.Contains(userMessages, m => m.Text != null && m.Text.Contains("Question 1"));
        Assert.Contains(userMessages, m => m.Text != null && m.Text.Contains("Question 2"));
        Assert.Contains(userMessages, m => m.Text != null && m.Text.Contains("Question 3"));
    }

    [Fact]
    public async Task Agent_DifferentSessions_HaveSeparateHistory()
    {
        // Arrange
        var receivedMessages = new List<List<ChatMessage>>();
        var chatClientMock = new Mock<IChatClient>();
        var metadata = new ChatClientMetadata("test");
        chatClientMock.Setup(c => c.GetService(typeof(ChatClientMetadata), null))
            .Returns(metadata);

        chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((msgs, opts, ct) =>
            {
                receivedMessages.Add(msgs.ToList());
            })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Response")));

        var agent = NanoBotAgentFactory.Create(
            chatClientMock.Object,
            _workspaceMock.Object,
            _skillsLoaderMock.Object);

        var session1 = await agent.CreateSessionAsync();
        var session2 = await agent.CreateSessionAsync();

        // Act
        await agent.RunAsync("Session 1 message", session1);
        await agent.RunAsync("Session 2 message", session2);

        // Assert - Second session should not have first session's history
        Assert.Equal(2, receivedMessages.Count);
        
        var session2Messages = receivedMessages[1].Where(m => m.Role == ChatRole.User).ToList();
        var session2UserMessages = session2Messages.Where(m => m.Text != null && m.Text.Contains("Session")).ToList();
        
        // Should only have "Session 2 message", not "Session 1 message"
        Assert.Single(session2UserMessages);
        Assert.Contains("Session 2 message", session2UserMessages[0].Text);
        Assert.DoesNotContain(session2Messages, m => m.Text != null && m.Text.Contains("Session 1 message"));
    }
}
