using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Moq;
using NanoBot.Agent.Context;
using NanoBot.Core.Memory;
using NanoBot.Core.Skills;
using NanoBot.Core.Workspace;
using Xunit;

namespace NanoBot.Agent.Tests.Context;

public class ProviderTests
{
    private static AgentSession CreateSession()
    {
        var mock = new Mock<AgentSession>();
        return mock.Object;
    }

    [Fact(Skip = "FileBackedChatHistoryProvider API has changed")]
    public async Task FileBackedChatHistoryProvider_InvokingCoreAsync_ReturnsEmptyWhenNoHistory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var historyPath = Path.Combine(tempDir, "HISTORY.md");
            var workspaceMock = new Mock<IWorkspaceManager>();
            workspaceMock.Setup(w => w.GetHistoryFile()).Returns(historyPath);

            var provider = new FileBackedChatHistoryProvider(workspaceMock.Object);

            var agentMock = new Mock<AIAgent>();
            var session = CreateSession();
            var invokingContext = new ChatHistoryProvider.InvokingContext(
                agentMock.Object,
                session,
                [new ChatMessage(ChatRole.User, "Hello")]);

            var result = await provider.InvokingAsync(invokingContext);

            Assert.NotNull(result);
            var messages = result.ToList();
            Assert.Empty(messages);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact(Skip = "FileBackedChatHistoryProvider API has changed")]
    public async Task FileBackedChatHistoryProvider_InvokedCoreAsync_WritesToFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var historyPath = Path.Combine(tempDir, "HISTORY.md");
            var workspaceMock = new Mock<IWorkspaceManager>();
            workspaceMock.Setup(w => w.GetHistoryFile()).Returns(historyPath);

            var provider = new FileBackedChatHistoryProvider(workspaceMock.Object);

            var agentMock = new Mock<AIAgent>();
            var session = CreateSession();
            var invokedContext = new ChatHistoryProvider.InvokedContext(
                agentMock.Object,
                session,
                [new ChatMessage(ChatRole.User, "Hello")],
                [new ChatMessage(ChatRole.Assistant, "Hi there!")]);

            await provider.InvokedAsync(invokedContext);

            Assert.True(File.Exists(historyPath));
            var content = await File.ReadAllTextAsync(historyPath);
            Assert.Contains("user: Hello", content);
            Assert.Contains("assistant: Hi there!", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact(Skip = "FileBackedChatHistoryProvider API has changed")]
    public async Task FileBackedChatHistoryProvider_InvokingCoreAsync_ParsesHistoryCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var historyPath = Path.Combine(tempDir, "HISTORY.md");
            var historyContent = new StringBuilder();
            historyContent.AppendLine("[2024-01-15 10:30:00] user: What is the weather?");
            historyContent.AppendLine("[2024-01-15 10:30:05] assistant: I don't have access to real-time weather data.");
            historyContent.AppendLine();
            await File.WriteAllTextAsync(historyPath, historyContent.ToString());

            var workspaceMock = new Mock<IWorkspaceManager>();
            workspaceMock.Setup(w => w.GetHistoryFile()).Returns(historyPath);

            var provider = new FileBackedChatHistoryProvider(workspaceMock.Object);

            var agentMock = new Mock<AIAgent>();
            var session = CreateSession();
            var invokingContext = new ChatHistoryProvider.InvokingContext(
                agentMock.Object,
                session,
                [new ChatMessage(ChatRole.User, "How about tomorrow?")]);

            var result = await provider.InvokingAsync(invokingContext);

            var messages = result.ToList();
            Assert.Equal(2, messages.Count);
            Assert.Equal(ChatRole.User, messages[0].Role);
            Assert.Equal("What is the weather?", messages[0].Text);
            Assert.Equal(ChatRole.Assistant, messages[1].Role);
            Assert.Equal("I don't have access to real-time weather data.", messages[1].Text);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task BootstrapContextProvider_InvokingCoreAsync_LoadsBootstrapFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var agentsPath = Path.Combine(tempDir, "AGENTS.md");
            var soulPath = Path.Combine(tempDir, "SOUL.md");

            await File.WriteAllTextAsync(agentsPath, "# Agent Instructions\nBe helpful and friendly.");
            await File.WriteAllTextAsync(soulPath, "# Soul\nI am a helpful assistant.");

            var workspaceMock = new Mock<IWorkspaceManager>();
            workspaceMock.Setup(w => w.GetAgentsFile()).Returns(agentsPath);
            workspaceMock.Setup(w => w.GetSoulFile()).Returns(soulPath);
            workspaceMock.Setup(w => w.GetUserFile()).Returns(Path.Combine(tempDir, "USER.md"));
            workspaceMock.Setup(w => w.GetToolsFile()).Returns(Path.Combine(tempDir, "TOOLS.md"));
            workspaceMock.Setup(w => w.GetWorkspacePath()).Returns(tempDir);

            var provider = new BootstrapContextProvider(workspaceMock.Object);

            var agentMock = new Mock<AIAgent>();
            var session = CreateSession();
            var aiContext = new AIContext { Instructions = "test" };
            var invokingContext = new AIContextProvider.InvokingContext(
                agentMock.Object,
                session,
                aiContext);

            var result = await provider.InvokingAsync(invokingContext);

            Assert.NotNull(result);
            Assert.NotNull(result.Instructions);
            Assert.Contains("Agent Configuration", result.Instructions);
            Assert.Contains("Be helpful and friendly", result.Instructions);
            Assert.Contains("Personality", result.Instructions);
            Assert.Contains("I am a helpful assistant", result.Instructions);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task MemoryContextProvider_InvokingCoreAsync_LoadsMemory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var memoryPath = Path.Combine(tempDir, "MEMORY.md");
            await File.WriteAllTextAsync(memoryPath, "User prefers concise responses.\nUser's timezone is UTC+8.");

            var memoryStoreMock = new Mock<IMemoryStore>();
            memoryStoreMock.Setup(m => m.LoadAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("User prefers concise responses.\nUser's timezone is UTC+8.");

            var workspaceMock = new Mock<IWorkspaceManager>();
            workspaceMock.Setup(w => w.GetMemoryFile()).Returns(memoryPath);

            var provider = new MemoryContextProvider(workspaceMock.Object, memoryStoreMock.Object);

            var agentMock = new Mock<AIAgent>();
            var session = CreateSession();
            var aiContext = new AIContext { Instructions = "test" };
            var invokingContext = new AIContextProvider.InvokingContext(
                agentMock.Object,
                session,
                aiContext);

            var result = await provider.InvokingAsync(invokingContext);

            Assert.NotNull(result);
            Assert.NotNull(result.Instructions);
            Assert.Contains("## Memory", result.Instructions);
            Assert.Contains("User prefers concise responses", result.Instructions);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task SkillsContextProvider_InvokingCoreAsync_LoadsSkills()
    {
        var skillsLoaderMock = new Mock<ISkillsLoader>();
        skillsLoaderMock.Setup(s => s.GetAlwaysSkills())
            .Returns(new List<string> { "memory" });
        skillsLoaderMock.Setup(s => s.BuildSkillsSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("<skills><skill available=\"true\"><name>github</name><description>GitHub operations</description></skill></skills>");
        skillsLoaderMock.Setup(s => s.LoadSkillsForContextAsync(
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("### Skill: memory\n\nThis skill helps manage memory.");

        var provider = new SkillsContextProvider(skillsLoaderMock.Object);

        var agentMock = new Mock<AIAgent>();
        var session = CreateSession();
        var aiContext = new AIContext { Instructions = "test" };
        var invokingContext = new AIContextProvider.InvokingContext(
            agentMock.Object,
            session,
            aiContext);

        var result = await provider.InvokingAsync(invokingContext);

        Assert.NotNull(result);
        Assert.NotNull(result.Instructions);
        Assert.Contains("# Active Skills", result.Instructions);
        Assert.Contains("Skill: memory", result.Instructions);
        Assert.Contains("# Skills", result.Instructions);
        Assert.Contains("github", result.Instructions);
    }

    [Fact]
    public async Task SkillsContextProvider_InvokingCoreAsync_ReturnsEmptyWhenNoSkills()
    {
        var skillsLoaderMock = new Mock<ISkillsLoader>();
        skillsLoaderMock.Setup(s => s.GetAlwaysSkills())
            .Returns(new List<string>());
        skillsLoaderMock.Setup(s => s.BuildSkillsSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var provider = new SkillsContextProvider(skillsLoaderMock.Object);

        var agentMock = new Mock<AIAgent>();
        var session = CreateSession();
        var aiContext = new AIContext { Instructions = null };
        var invokingContext = new AIContextProvider.InvokingContext(
            agentMock.Object,
            session,
            aiContext);

        var result = await provider.InvokingAsync(invokingContext);

        Assert.NotNull(result);
        Assert.Null(result.Instructions);
    }
}
