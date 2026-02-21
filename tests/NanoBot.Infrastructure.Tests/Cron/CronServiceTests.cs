using Microsoft.Extensions.Logging;
using NanoBot.Core.Cron;
using NanoBot.Infrastructure.Cron;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Cron;

public class CronServiceTests : IDisposable
{
    private readonly string _testStorePath;
    private readonly ILogger<CronService> _logger;

    public CronServiceTests()
    {
        _testStorePath = Path.Combine(Path.GetTempPath(), $"cron_test_{Guid.NewGuid():N}.json");
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _logger = loggerFactory.CreateLogger<CronService>();
    }

    public void Dispose()
    {
        if (File.Exists(_testStorePath))
        {
            File.Delete(_testStorePath);
        }
    }

    [Fact]
    public void Constructor_CreatesInstance()
    {
        var service = new CronService(_testStorePath, _logger);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task StartAsync_InitializesService()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var status = service.GetStatus();
        Assert.True(status.Running);

        await service.StopAsync();
    }

    [Fact]
    public async Task StopAsync_StopsService()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();
        await service.StopAsync();

        var status = service.GetStatus();
        Assert.False(status.Running);
    }

    [Fact]
    public async Task AddJob_ReturnsJobWithId()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var definition = new CronJobDefinition
        {
            Name = "Test Job",
            Schedule = new CronSchedule { Kind = CronScheduleKind.Every, EveryMs = 60000 },
            Message = "Test message"
        };

        var job = service.AddJob(definition);

        Assert.NotNull(job);
        Assert.NotNull(job.Id);
        Assert.Equal("Test Job", job.Name);
        Assert.True(job.Enabled);

        await service.StopAsync();
    }

    [Fact]
    public async Task AddJob_PersistsToStore()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var definition = new CronJobDefinition
        {
            Name = "Persisted Job",
            Schedule = new CronSchedule { Kind = CronScheduleKind.Every, EveryMs = 60000 },
            Message = "Test"
        };

        service.AddJob(definition);
        await service.StopAsync();

        var service2 = new CronService(_testStorePath, _logger);
        await service2.StartAsync();

        var jobs = service2.ListJobs(true);
        Assert.Single(jobs);
        Assert.Equal("Persisted Job", jobs[0].Name);

        await service2.StopAsync();
    }

    [Fact]
    public async Task RemoveJob_RemovesFromList()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var definition = new CronJobDefinition
        {
            Name = "To Remove",
            Schedule = new CronSchedule { Kind = CronScheduleKind.Every, EveryMs = 60000 },
            Message = "Test"
        };

        var job = service.AddJob(definition);
        var removed = service.RemoveJob(job.Id);

        Assert.True(removed);
        Assert.Empty(service.ListJobs(true));

        await service.StopAsync();
    }

    [Fact]
    public async Task EnableJob_TogglesEnabled()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var definition = new CronJobDefinition
        {
            Name = "Toggle Test",
            Schedule = new CronSchedule { Kind = CronScheduleKind.Every, EveryMs = 60000 },
            Message = "Test"
        };

        var job = service.AddJob(definition);
        Assert.True(job.Enabled);

        var disabledJob = service.EnableJob(job.Id, false);
        Assert.False(disabledJob?.Enabled);

        var enabledJob = service.EnableJob(job.Id, true);
        Assert.True(enabledJob?.Enabled);

        await service.StopAsync();
    }

    [Fact]
    public async Task ListJobs_FiltersByEnabled()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var def1 = new CronJobDefinition
        {
            Name = "Enabled Job",
            Schedule = new CronSchedule { Kind = CronScheduleKind.Every, EveryMs = 60000 },
            Message = "Test"
        };

        var def2 = new CronJobDefinition
        {
            Name = "Disabled Job",
            Schedule = new CronSchedule { Kind = CronScheduleKind.Every, EveryMs = 60000 },
            Message = "Test"
        };

        var job1 = service.AddJob(def1);
        var job2 = service.AddJob(def2);
        service.EnableJob(job2.Id, false);

        var allJobs = service.ListJobs(true);
        Assert.Equal(2, allJobs.Count);

        var enabledJobs = service.ListJobs(false);
        Assert.Single(enabledJobs);
        Assert.Equal("Enabled Job", enabledJobs[0].Name);

        await service.StopAsync();
    }

    [Fact]
    public async Task GetStatus_ReturnsCorrectCounts()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var def = new CronJobDefinition
        {
            Name = "Status Test",
            Schedule = new CronSchedule { Kind = CronScheduleKind.Every, EveryMs = 60000 },
            Message = "Test"
        };

        service.AddJob(def);

        var status = service.GetStatus();
        Assert.True(status.Running);
        Assert.Equal(1, status.TotalJobs);
        Assert.Equal(1, status.EnabledJobs);

        await service.StopAsync();
    }

    [Fact]
    public async Task AtSchedule_ComputesNextRunCorrectly()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var futureMs = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var definition = new CronJobDefinition
        {
            Name = "One-time Job",
            Schedule = new CronSchedule { Kind = CronScheduleKind.At, AtMs = futureMs },
            Message = "Test"
        };

        var job = service.AddJob(definition);
        Assert.NotNull(job.State.NextRunAtMs);
        Assert.True(job.State.NextRunAtMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await service.StopAsync();
    }

    [Fact]
    public async Task EverySchedule_ComputesNextRunCorrectly()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var definition = new CronJobDefinition
        {
            Name = "Recurring Job",
            Schedule = new CronSchedule { Kind = CronScheduleKind.Every, EveryMs = 3600000 },
            Message = "Test"
        };

        var job = service.AddJob(definition);
        Assert.NotNull(job.State.NextRunAtMs);

        var expectedNextRun = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var tolerance = 10000;

        Assert.True(Math.Abs(job.State.NextRunAtMs.Value - expectedNextRun) < tolerance);

        await service.StopAsync();
    }

    [Fact]
    public async Task JobExecuted_EventIsRaised()
    {
        var tcs = new TaskCompletionSource<CronJobEventArgs>();
        var service = new CronService(_testStorePath, _logger, async job =>
        {
            return "Executed";
        });

        service.JobExecuted += (sender, args) => tcs.TrySetResult(args);

        await service.StartAsync();

        var definition = new CronJobDefinition
        {
            Name = "Event Test",
            Schedule = new CronSchedule { Kind = CronScheduleKind.Every, EveryMs = 100 },
            Message = "Test"
        };

        var job = service.AddJob(definition);
        await service.RunJobAsync(job.Id);

        var eventArgs = await Task.WhenAny(tcs.Task, Task.Delay(5000)) as Task<CronJobEventArgs>;
        Assert.NotNull(eventArgs);
        Assert.True(eventArgs.Result.Success);

        await service.StopAsync();
    }

    [Fact]
    public async Task AddJob_WithValidTimezone_Succeeds()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var definition = new CronJobDefinition
        {
            Name = "Timezone Test",
            Schedule = new CronSchedule 
            { 
                Kind = CronScheduleKind.Cron,
                Expression = "0 9 * * *",
                TimeZone = "America/New_York"
            },
            Message = "Test"
        };

        var job = service.AddJob(definition);

        Assert.NotNull(job);
        Assert.Equal("America/New_York", job.Schedule.TimeZone);

        await service.StopAsync();
    }

    [Fact]
    public async Task AddJob_WithInvalidTimezone_ThrowsArgumentException()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var definition = new CronJobDefinition
        {
            Name = "Invalid Timezone Test",
            Schedule = new CronSchedule 
            { 
                Kind = CronScheduleKind.Cron,
                Expression = "0 9 * * *",
                TimeZone = "Invalid/Timezone_That_Does_Not_Exist"
            },
            Message = "Test"
        };

        Assert.Throws<ArgumentException>(() => service.AddJob(definition));

        await service.StopAsync();
    }

    [Fact]
    public async Task AddJob_WithoutTimezone_Succeeds()
    {
        var service = new CronService(_testStorePath, _logger);
        await service.StartAsync();

        var definition = new CronJobDefinition
        {
            Name = "No Timezone Test",
            Schedule = new CronSchedule 
            { 
                Kind = CronScheduleKind.Cron,
                Expression = "0 9 * * *"
            },
            Message = "Test"
        };

        var job = service.AddJob(definition);

        Assert.NotNull(job);
        Assert.Null(job.Schedule.TimeZone);

        await service.StopAsync();
    }
}
