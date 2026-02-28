using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using Moq;
using NanoBot.Core.Heartbeat;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Heartbeat;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Heartbeat;

public class HeartbeatServiceTests : IDisposable
{
    private readonly Mock<IWorkspaceManager> _workspaceManagerMock;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly string _testHeartbeatPath;

    private static IChatClient CreateChatClientThatRuns(string tasks)
    {
        var chatClient = new Mock<IChatClient>();

        chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var call = new FunctionCallContent(
                    callId: "call_1",
                    name: "heartbeat",
                    arguments: new Dictionary<string, object?>
                    {
                        ["action"] = "run",
                        ["tasks"] = tasks
                    });

                return new ChatResponse(new ChatMessage(ChatRole.Assistant, [call]));
            });

        return chatClient.Object;
    }

    public HeartbeatServiceTests()
    {
        _workspaceManagerMock = new Mock<IWorkspaceManager>();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _logger = loggerFactory.CreateLogger<HeartbeatService>();
        _testHeartbeatPath = Path.Combine(Path.GetTempPath(), $"heartbeat_test_{Guid.NewGuid():N}.md");
    }

    public void Dispose()
    {
        if (File.Exists(_testHeartbeatPath))
        {
            File.Delete(_testHeartbeatPath);
        }
    }

    [Fact]
    public void Constructor_CreatesInstance()
    {
        var service = new HeartbeatService(_workspaceManagerMock.Object, chatClient: null, _logger);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task StartAsync_WhenEnabled_StartsService()
    {
        var service = new HeartbeatService(
            _workspaceManagerMock.Object,
            chatClient: null,
            _logger,
            enabled: true);

        await service.StartAsync();

        var status = service.GetStatus();
        Assert.True(status.Running);

        await service.StopAsync();
    }

    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNotStart()
    {
        var service = new HeartbeatService(
            _workspaceManagerMock.Object,
            chatClient: null,
            _logger,
            enabled: false);

        await service.StartAsync();

        var status = service.GetStatus();
        Assert.False(status.Running);
    }

    [Fact]
    public async Task StopAsync_StopsService()
    {
        var service = new HeartbeatService(_workspaceManagerMock.Object, chatClient: null, _logger);
        await service.StartAsync();
        await service.StopAsync();

        var status = service.GetStatus();
        Assert.False(status.Running);
    }

    [Fact]
    public void AddJob_ReturnsJobWithId()
    {
        var service = new HeartbeatService(_workspaceManagerMock.Object, chatClient: null, _logger);

        var definition = new HeartbeatDefinition
        {
            Name = "Test Job",
            IntervalSeconds = 60,
            Message = "Test message"
        };

        var job = service.AddJob(definition);

        Assert.NotNull(job);
        Assert.NotNull(job.Id);
        Assert.Equal("Test Job", job.Name);
        Assert.Equal(60, job.IntervalSeconds);
    }

    [Fact]
    public void RemoveJob_RemovesFromList()
    {
        var service = new HeartbeatService(_workspaceManagerMock.Object, chatClient: null, _logger);

        var definition = new HeartbeatDefinition
        {
            Name = "To Remove",
            IntervalSeconds = 60,
            Message = "Test"
        };

        var job = service.AddJob(definition);
        var removed = service.RemoveJob(job.Id);

        Assert.True(removed);
        Assert.Empty(service.ListJobs());
    }

    [Fact]
    public void ListJobs_ReturnsAllJobs()
    {
        var service = new HeartbeatService(_workspaceManagerMock.Object, chatClient: null, _logger);

        service.AddJob(new HeartbeatDefinition { Name = "Job 1", IntervalSeconds = 60, Message = "Test" });
        service.AddJob(new HeartbeatDefinition { Name = "Job 2", IntervalSeconds = 120, Message = "Test" });

        var jobs = service.ListJobs();
        Assert.Equal(2, jobs.Count);
    }

    [Fact]
    public void GetStatus_ReturnsCorrectCounts()
    {
        var service = new HeartbeatService(_workspaceManagerMock.Object, chatClient: null, _logger);

        service.AddJob(new HeartbeatDefinition { Name = "Job 1", IntervalSeconds = 60, Message = "Test" });

        var status = service.GetStatus();
        Assert.Equal(1, status.ActiveJobs);
    }

    [Fact]
    public async Task TriggerNowAsync_WithCallback_ReturnsResponse()
    {
        var expectedResponse = "HEARTBEAT_OK";
        var chatClient = CreateChatClientThatRuns("- [ ] Test task");
        var service = new HeartbeatService(
            _workspaceManagerMock.Object,
            chatClient,
            _logger,
            onHeartbeat: prompt => Task.FromResult(expectedResponse));

        _workspaceManagerMock.Setup(x => x.GetHeartbeatFile()).Returns(_testHeartbeatPath);
        await File.WriteAllTextAsync(_testHeartbeatPath, "- [ ] Test task");

        var response = await service.TriggerNowAsync();

        Assert.Equal(expectedResponse, response);
    }

    [Fact]
    public async Task TriggerNowAsync_WithoutCallback_ReturnsNull()
    {
        var service = new HeartbeatService(_workspaceManagerMock.Object, chatClient: null, _logger);

        var response = await service.TriggerNowAsync();

        Assert.Null(response);
    }

    [Fact]
    public async Task HeartbeatExecuted_EventIsRaised()
    {
        var tcs = new TaskCompletionSource<HeartbeatEventArgs>();
        var chatClient = CreateChatClientThatRuns("- [ ] Test task");
        var service = new HeartbeatService(
            _workspaceManagerMock.Object,
            chatClient,
            _logger,
            onHeartbeat: prompt => Task.FromResult("HEARTBEAT_OK"));

        service.HeartbeatExecuted += (sender, args) => tcs.TrySetResult(args);

        _workspaceManagerMock.Setup(x => x.GetHeartbeatFile()).Returns(_testHeartbeatPath);
        await File.WriteAllTextAsync(_testHeartbeatPath, "- [ ] Test task");

        await service.TriggerNowAsync();

        var eventArgs = await Task.WhenAny(tcs.Task, Task.Delay(5000)) as Task<HeartbeatEventArgs>;
        Assert.NotNull(eventArgs);
        Assert.True(eventArgs.Result.Success);
    }

    [Fact]
    public async Task Heartbeat_SkipsWhenFileEmpty()
    {
        var executed = false;
        var service = new HeartbeatService(
            _workspaceManagerMock.Object,
            chatClient: null,
            _logger,
            intervalSeconds: 1,
            onHeartbeat: prompt =>
            {
                executed = true;
                return Task.FromResult("HEARTBEAT_OK");
            });

        _workspaceManagerMock.Setup(x => x.GetHeartbeatFile()).Returns(_testHeartbeatPath);
        await File.WriteAllTextAsync(_testHeartbeatPath, "");

        await service.StartAsync();
        await Task.Delay(1500);

        Assert.False(executed);

        await service.StopAsync();
    }

    [Fact]
    public async Task Heartbeat_SkipsWhenFileHasOnlyComments()
    {
        var executed = false;
        var service = new HeartbeatService(
            _workspaceManagerMock.Object,
            chatClient: null,
            _logger,
            intervalSeconds: 1,
            onHeartbeat: prompt =>
            {
                executed = true;
                return Task.FromResult("HEARTBEAT_OK");
            });

        _workspaceManagerMock.Setup(x => x.GetHeartbeatFile()).Returns(_testHeartbeatPath);
        await File.WriteAllTextAsync(_testHeartbeatPath, """
# Heartbeat Tasks

<!-- Comments only -->
- [ ]

""");

        await service.StartAsync();
        await Task.Delay(1500);

        Assert.False(executed);

        await service.StopAsync();
    }
}
