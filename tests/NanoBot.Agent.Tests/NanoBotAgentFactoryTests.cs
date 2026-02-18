using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Core.Memory;
using NanoBot.Core.Skills;
using NanoBot.Core.Workspace;
using Xunit;

namespace NanoBot.Agent.Tests;

public class NanoBotAgentFactoryTests
{
    [Fact]
    public void Create_ReturnsValidChatClientAgent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var chatClientMock = CreateChatClientMock();
            var workspaceMock = CreateWorkspaceMock(tempDir);
            var skillsLoaderMock = CreateSkillsLoaderMock();

            var agent = NanoBotAgentFactory.Create(
                chatClientMock.Object,
                workspaceMock.Object,
                skillsLoaderMock.Object);

            Assert.NotNull(agent);
            Assert.Equal("NanoBot", agent.Name);
            Assert.Equal("A personal AI assistant", agent.Description);
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
    public void Create_WithCustomOptions_UsesCustomValues()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var chatClientMock = CreateChatClientMock();
            var workspaceMock = CreateWorkspaceMock(tempDir);
            var skillsLoaderMock = CreateSkillsLoaderMock();

            var options = new AgentOptions
            {
                Name = "CustomBot",
                Description = "Custom description",
                MaxHistoryEntries = 50
            };

            var agent = NanoBotAgentFactory.Create(
                chatClientMock.Object,
                workspaceMock.Object,
                skillsLoaderMock.Object,
                options: options);

            Assert.NotNull(agent);
            Assert.Equal("CustomBot", agent.Name);
            Assert.Equal("Custom description", agent.Description);
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
    public void Create_WithTools_PassesToolsToAgent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var chatClientMock = CreateChatClientMock();
            var workspaceMock = CreateWorkspaceMock(tempDir);
            var skillsLoaderMock = CreateSkillsLoaderMock();

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(() => "test", new AIFunctionFactoryOptions { Name = "test_tool" })
            };

            var agent = NanoBotAgentFactory.Create(
                chatClientMock.Object,
                workspaceMock.Object,
                skillsLoaderMock.Object,
                tools: tools);

            Assert.NotNull(agent);
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
    public void Create_WithMemoryStore_PassesMemoryStoreToProvider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var chatClientMock = CreateChatClientMock();
            var workspaceMock = CreateWorkspaceMock(tempDir);
            var skillsLoaderMock = CreateSkillsLoaderMock();
            var memoryStoreMock = new Mock<IMemoryStore>();

            var agent = NanoBotAgentFactory.Create(
                chatClientMock.Object,
                workspaceMock.Object,
                skillsLoaderMock.Object,
                memoryStore: memoryStoreMock.Object);

            Assert.NotNull(agent);
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
    public void BuildInstructions_ContainsIdentitySection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspaceMock = CreateWorkspaceMock(tempDir);

            var instructions = NanoBotAgentFactory.BuildInstructions(workspaceMock.Object);

            Assert.Contains("NanoBot", instructions);
            Assert.Contains("Current Time", instructions);
            Assert.Contains("Runtime", instructions);
            Assert.Contains("Workspace", instructions);
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
    public void BuildInstructions_WithCustomName_UsesCustomName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspaceMock = CreateWorkspaceMock(tempDir);
            var options = new AgentOptions { Name = "MyCustomAgent" };

            var instructions = NanoBotAgentFactory.BuildInstructions(workspaceMock.Object, options);

            Assert.Contains("MyCustomAgent", instructions);
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
    public void BuildInstructions_LoadsAgentsFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var agentsPath = Path.Combine(tempDir, "AGENTS.md");
            File.WriteAllText(agentsPath, "# Agent Instructions\nBe helpful and friendly.");

            var workspaceMock = CreateWorkspaceMock(tempDir);

            var instructions = NanoBotAgentFactory.BuildInstructions(workspaceMock.Object);

            Assert.Contains("Agent Configuration", instructions);
            Assert.Contains("Be helpful and friendly", instructions);
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
    public void BuildInstructions_LoadsSoulFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var soulPath = Path.Combine(tempDir, "SOUL.md");
            File.WriteAllText(soulPath, "# Soul\nI am a helpful assistant.");

            var workspaceMock = CreateWorkspaceMock(tempDir);

            var instructions = NanoBotAgentFactory.BuildInstructions(workspaceMock.Object);

            Assert.Contains("Personality", instructions);
            Assert.Contains("I am a helpful assistant", instructions);
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
    public void Create_ThrowsOnNullChatClient()
    {
        var workspaceMock = new Mock<IWorkspaceManager>();
        var skillsLoaderMock = new Mock<ISkillsLoader>();

        Assert.Throws<ArgumentNullException>(() =>
            NanoBotAgentFactory.Create(null!, workspaceMock.Object, skillsLoaderMock.Object));
    }

    [Fact]
    public void Create_ThrowsOnNullWorkspace()
    {
        var chatClientMock = new Mock<IChatClient>();
        var skillsLoaderMock = new Mock<ISkillsLoader>();

        Assert.Throws<ArgumentNullException>(() =>
            NanoBotAgentFactory.Create(chatClientMock.Object, null!, skillsLoaderMock.Object));
    }

    [Fact]
    public void Create_ThrowsOnNullSkillsLoader()
    {
        var chatClientMock = new Mock<IChatClient>();
        var workspaceMock = new Mock<IWorkspaceManager>();

        Assert.Throws<ArgumentNullException>(() =>
            NanoBotAgentFactory.Create(chatClientMock.Object, workspaceMock.Object, null!));
    }

    private static Mock<IChatClient> CreateChatClientMock()
    {
        var mock = new Mock<IChatClient>();
        var metadata = new ChatClientMetadata("test");
        mock.Setup(c => c.GetService(typeof(ChatClientMetadata), null))
            .Returns(metadata);
        return mock;
    }

    private static Mock<IWorkspaceManager> CreateWorkspaceMock(string tempDir)
    {
        var mock = new Mock<IWorkspaceManager>();
        mock.Setup(w => w.GetWorkspacePath()).Returns(tempDir);
        mock.Setup(w => w.GetAgentsFile()).Returns(Path.Combine(tempDir, "AGENTS.md"));
        mock.Setup(w => w.GetSoulFile()).Returns(Path.Combine(tempDir, "SOUL.md"));
        mock.Setup(w => w.GetUserFile()).Returns(Path.Combine(tempDir, "USER.md"));
        mock.Setup(w => w.GetToolsFile()).Returns(Path.Combine(tempDir, "TOOLS.md"));
        mock.Setup(w => w.GetMemoryFile()).Returns(Path.Combine(tempDir, "memory", "MEMORY.md"));
        mock.Setup(w => w.GetHistoryFile()).Returns(Path.Combine(tempDir, "memory", "HISTORY.md"));
        mock.Setup(w => w.GetMemoryPath()).Returns(Path.Combine(tempDir, "memory"));
        mock.Setup(w => w.GetSkillsPath()).Returns(Path.Combine(tempDir, "skills"));
        return mock;
    }

    private static Mock<ISkillsLoader> CreateSkillsLoaderMock()
    {
        var mock = new Mock<ISkillsLoader>();
        mock.Setup(s => s.GetAlwaysSkills()).Returns(new List<string>());
        mock.Setup(s => s.BuildSkillsSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        return mock;
    }
}
