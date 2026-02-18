using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent;
using NanoBot.Core.Bus;
using NanoBot.Core.Cron;
using NanoBot.Core.Memory;
using NanoBot.Core.Skills;
using NanoBot.Core.Subagents;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Bus;
using NanoBot.Infrastructure.Memory;
using NanoBot.Infrastructure.Skills;
using NanoBot.Infrastructure.Workspace;

namespace NanoBot.Integration.Tests;

public class TestFixture : IAsyncDisposable
{
    public string TestDirectory { get; }
    public string WorkspaceDirectory { get; }
    public string SessionsDirectory { get; }
    public ServiceProvider Services { get; }
    public MockChatClient ChatClient { get; }
    public IMessageBus MessageBus { get; }
    public IWorkspaceManager WorkspaceManager { get; }
    public ISkillsLoader SkillsLoader { get; }
    public ChatClientAgent Agent { get; }
    public ISessionManager SessionManager { get; }

    public TestFixture()
    {
        TestDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_integration_{Guid.NewGuid():N}");
        WorkspaceDirectory = Path.Combine(TestDirectory, "workspace");
        SessionsDirectory = Path.Combine(TestDirectory, "sessions");

        Directory.CreateDirectory(TestDirectory);
        Directory.CreateDirectory(WorkspaceDirectory);
        Directory.CreateDirectory(SessionsDirectory);
        Directory.CreateDirectory(Path.Combine(WorkspaceDirectory, "memory"));
        Directory.CreateDirectory(Path.Combine(WorkspaceDirectory, "skills"));

        CreateDefaultWorkspaceFiles();

        ChatClient = new MockChatClient("test-provider");
        MessageBus = new MessageBus();

        var workspaceMock = CreateWorkspaceMock();
        WorkspaceManager = workspaceMock.Object;

        var skillsLoaderMock = CreateSkillsLoaderMock();
        SkillsLoader = skillsLoaderMock.Object;

        var loggerFactory = LoggerFactory.Create(builder => { });

        Agent = NanoBotAgentFactory.Create(
            ChatClient,
            WorkspaceManager,
            SkillsLoader,
            tools: null,
            loggerFactory: loggerFactory);

        SessionManager = new SessionManager(Agent, WorkspaceManager);

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(ChatClient);
        services.AddSingleton<IMessageBus>(MessageBus);
        services.AddSingleton<IWorkspaceManager>(WorkspaceManager);
        services.AddSingleton<ISkillsLoader>(SkillsLoader);
        services.AddSingleton(Agent);
        services.AddSingleton(SessionManager);
        services.AddSingleton(loggerFactory);
        services.AddLogging();

        Services = services.BuildServiceProvider();
    }

    private void CreateDefaultWorkspaceFiles()
    {
        File.WriteAllText(
            Path.Combine(WorkspaceDirectory, "AGENTS.md"),
            "# Test Agent Configuration\n\nThis is a test agent configuration.");

        File.WriteAllText(
            Path.Combine(WorkspaceDirectory, "SOUL.md"),
            "# Test Personality\n\nYou are a helpful test assistant.");

        File.WriteAllText(
            Path.Combine(WorkspaceDirectory, "memory", "MEMORY.md"),
            "# Memory\n\nTest memory content.");
    }

    private Mock<IWorkspaceManager> CreateWorkspaceMock()
    {
        var mock = new Mock<IWorkspaceManager>();
        mock.Setup(w => w.GetWorkspacePath()).Returns(WorkspaceDirectory);
        mock.Setup(w => w.GetSessionsPath()).Returns(SessionsDirectory);
        mock.Setup(w => w.GetAgentsFile()).Returns(Path.Combine(WorkspaceDirectory, "AGENTS.md"));
        mock.Setup(w => w.GetSoulFile()).Returns(Path.Combine(WorkspaceDirectory, "SOUL.md"));
        mock.Setup(w => w.GetMemoryFile()).Returns(Path.Combine(WorkspaceDirectory, "memory", "MEMORY.md"));
        mock.Setup(w => w.GetHistoryFile()).Returns(Path.Combine(WorkspaceDirectory, "memory", "HISTORY.md"));
        mock.Setup(w => w.GetSkillsPath()).Returns(Path.Combine(WorkspaceDirectory, "skills"));
        mock.Setup(w => w.GetMemoryPath()).Returns(Path.Combine(WorkspaceDirectory, "memory"));
        mock.Setup(w => w.GetToolsFile()).Returns(Path.Combine(WorkspaceDirectory, "TOOLS.md"));
        mock.Setup(w => w.GetUserFile()).Returns(Path.Combine(WorkspaceDirectory, "USER.md"));
        mock.Setup(w => w.GetHeartbeatFile()).Returns(Path.Combine(WorkspaceDirectory, "HEARTBEAT.md"));
        mock.Setup(w => w.FileExists(It.IsAny<string>())).Returns(false);
        mock.Setup(w => w.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        mock.Setup(w => w.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.AppendFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(w => w.EnsureDirectory(It.IsAny<string>()));
        return mock;
    }

    private Mock<ISkillsLoader> CreateSkillsLoaderMock()
    {
        var mock = new Mock<ISkillsLoader>();
        mock.Setup(s => s.GetLoadedSkills()).Returns([]);
        mock.Setup(s => s.ListSkills(It.IsAny<bool>())).Returns([]);
        mock.Setup(s => s.GetAlwaysSkills()).Returns([]);
        mock.Setup(s => s.BuildSkillsSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        mock.Setup(s => s.LoadSkillsForContextAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        return mock;
    }

    public async ValueTask DisposeAsync()
    {
        Services.Dispose();
        (MessageBus as IDisposable)?.Dispose();

        var retries = 0;
        while (retries < 5)
        {
            try
            {
                if (Directory.Exists(TestDirectory))
                {
                    Directory.Delete(TestDirectory, recursive: true);
                }
                break;
            }
            catch (IOException)
            {
                await Task.Delay(100);
                retries++;
            }
        }
    }

    public async Task<InboundMessage> CreateInboundMessageAsync(
        string content,
        string channel = "test",
        string senderId = "user",
        string chatId = "chat1")
    {
        return await Task.FromResult(new InboundMessage
        {
            Channel = channel,
            SenderId = senderId,
            ChatId = chatId,
            Content = content
        });
    }

    public async Task PublishAndWaitForProcessingAsync(InboundMessage message, CancellationToken ct = default)
    {
        await MessageBus.PublishInboundAsync(message, ct);
    }
}
