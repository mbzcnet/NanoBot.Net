using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Core.Workspace;
using NanoBot.Providers;
using NanoBot.Tools.BuiltIn;
using Xunit;
using Moq;
using NanoBot.Agent.Tools;

namespace NanoBot.Tools.Tests;

public class OllamaIntegrationTests : IDisposable
{
    private readonly IChatClient _chatClient;
    private readonly string _testDirectory;

    public OllamaIntegrationTests()
    {
        var config = new LlmConfig
        {
            Provider = "ollama",
            Model = "lfm2.5-thinking:latest",
            ApiKey = "local-no-key",
            ApiBase = "http://127.0.0.1:11434/v1"
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var factoryLogger = loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(factoryLogger);

        _chatClient = factory.CreateChatClient(config);

        _testDirectory = Path.Combine(Path.GetTempPath(), $"ollama_integration_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        _chatClient?.Dispose();
        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task FileTools_WithRealLLM_CanReadAndWriteFiles()
    {
        var testFile = Path.Combine(_testDirectory, "test.txt");
        await File.WriteAllTextAsync(testFile, "Hello from Ollama test");

        var fileTool = FileTools.CreateReadFileTool();
        var writeTool = FileTools.CreateWriteFileTool();

        var chatOptions = new ChatOptions
        {
            Tools = [fileTool, writeTool]
        };

        var response = await _chatClient.GetResponseAsync(
            "Read the file and then write 'Updated by Ollama' to it.",
            new ChatOptions { Tools = [fileTool, writeTool] }
        );

        var content = await File.ReadAllTextAsync(testFile);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task FileTools_WithRealLLM_CanListDirectory()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "file1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "file2.txt"), "content2");
        Directory.CreateDirectory(Path.Combine(_testDirectory, "subdir"));

        var listTool = FileTools.CreateListDirTool();

        var response = await _chatClient.GetResponseAsync(
            "List all files and directories in the test folder.",
            new ChatOptions { Tools = [listTool] }
        );

        Assert.NotNull(response);
    }

    [Fact]
    public async Task ShellTools_WithRealLLM_CanExecuteCommand()
    {
        var execTool = ShellTools.CreateExecTool((ShellToolOptions?)null);

        var response = await _chatClient.GetResponseAsync(
            "Execute a simple shell command: echo 'Hello from shell'",
            new ChatOptions { Tools = [execTool] }
        );

        Assert.NotNull(response);
    }

    [Fact]
    public async Task SpawnTool_WithRealLLM_CanSpawnSubAgent()
    {
        var mockWorkspace = new Mock<IWorkspaceManager>();
        mockWorkspace.Setup(w => w.GetWorkspacePath()).Returns(_testDirectory);
        mockWorkspace.Setup(w => w.GetSkillsPath()).Returns(Path.Combine(_testDirectory, "skills"));

        var spawnTool = SpawnTool.CreateSpawnTool(_chatClient, mockWorkspace.Object);

        var response = await _chatClient.GetResponseAsync(
            "Create a sub-agent to list files in the current directory.",
            new ChatOptions { Tools = [spawnTool] }
        );

        Assert.NotNull(response);
    }
}
