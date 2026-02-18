using Microsoft.Extensions.Logging;
using NanoBot.Core.Heartbeat;
using NanoBot.Core.Workspace;

namespace NanoBot.Infrastructure.Heartbeat;

public class HeartbeatService : IHeartbeatService, IDisposable
{
    private const int DefaultIntervalSeconds = 30 * 60;
    private const string HeartbeatOkToken = "HEARTBEATOK";
    private const string HeartbeatPrompt = """Read HEARTBEAT.md in your workspace (if it exists). Follow any instructions or tasks listed there. If nothing needs attention, reply with just: HEARTBEAT_OK""";

    private readonly IWorkspaceManager _workspaceManager;
    private readonly Func<string, Task<string>>? _onHeartbeat;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly int _intervalSeconds;
    private readonly bool _enabled;

    private readonly List<HeartbeatJob> _jobs = new();
    private Timer? _timer;
    private bool _running;
    private bool _disposed;

    public event EventHandler<HeartbeatEventArgs>? HeartbeatExecuted;

    public HeartbeatService(
        IWorkspaceManager workspaceManager,
        ILogger<HeartbeatService> logger,
        Func<string, Task<string>>? onHeartbeat = null,
        int intervalSeconds = DefaultIntervalSeconds,
        bool enabled = true)
    {
        _workspaceManager = workspaceManager;
        _logger = logger;
        _onHeartbeat = onHeartbeat;
        _intervalSeconds = intervalSeconds;
        _enabled = enabled;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Heartbeat disabled");
            return Task.CompletedTask;
        }

        if (_running) return Task.CompletedTask;

        _running = true;
        _timer = new Timer(
            _ => _ = OnTimerTickAsync(),
            null,
            TimeSpan.FromSeconds(_intervalSeconds),
            TimeSpan.FromSeconds(_intervalSeconds));

        _logger.LogInformation("Heartbeat started (every {Interval}s)", _intervalSeconds);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_running) return Task.CompletedTask;

        _running = false;
        _timer?.Dispose();
        _timer = null;

        _logger.LogInformation("Heartbeat stopped");
        return Task.CompletedTask;
    }

    public HeartbeatJob AddJob(HeartbeatDefinition definition)
    {
        var job = new HeartbeatJob
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = definition.Name,
            IntervalSeconds = definition.IntervalSeconds,
            Message = definition.Message,
            ChannelId = definition.ChannelId,
            ChatId = definition.ChatId,
            Enabled = true,
            NextRunAt = DateTimeOffset.UtcNow.AddSeconds(definition.IntervalSeconds)
        };

        _jobs.Add(job);
        _logger.LogInformation("Added heartbeat job '{JobName}' ({JobId})", job.Name, job.Id);
        return job;
    }

    public bool RemoveJob(string jobId)
    {
        var removed = _jobs.RemoveAll(j => j.Id == jobId) > 0;
        if (removed)
        {
            _logger.LogInformation("Removed heartbeat job {JobId}", jobId);
        }
        return removed;
    }

    public IReadOnlyList<HeartbeatJob> ListJobs()
    {
        return _jobs.AsReadOnly();
    }

    public HeartbeatStatus GetStatus()
    {
        return new HeartbeatStatus
        {
            Running = _running,
            ActiveJobs = _jobs.Count(j => j.Enabled)
        };
    }

    public async Task<string?> TriggerNowAsync()
    {
        if (_onHeartbeat == null) return null;

        return await ExecuteHeartbeatAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
    }

    private async Task OnTimerTickAsync()
    {
        if (!_running) return;

        var content = await ReadHeartbeatFileAsync();

        if (IsHeartbeatEmpty(content))
        {
            _logger.LogDebug("Heartbeat: no tasks (HEARTBEAT.md empty)");
            return;
        }

        _logger.LogInformation("Heartbeat: checking for tasks...");

        await ExecuteHeartbeatAsync();
    }

    private async Task<string?> ReadHeartbeatFileAsync()
    {
        try
        {
            var heartbeatPath = _workspaceManager.GetHeartbeatFile();
            if (!File.Exists(heartbeatPath))
            {
                return null;
            }

            return await File.ReadAllTextAsync(heartbeatPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read HEARTBEAT.md");
            return null;
        }
    }

    private static bool IsHeartbeatEmpty(string? content)
    {
        if (string.IsNullOrEmpty(content)) return true;

        var skipPatterns = new HashSet<string> { "- [ ]", "* [ ]", "- [x]", "* [x]" };

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) ||
                trimmed.StartsWith('#') ||
                trimmed.StartsWith("<!--") ||
                skipPatterns.Contains(trimmed))
            {
                continue;
            }
            return false;
        }

        return true;
    }

    private async Task<string?> ExecuteHeartbeatAsync()
    {
        if (_onHeartbeat == null) return null;

        string? response = null;
        string? error = null;
        bool success = false;

        try
        {
            response = await _onHeartbeat(HeartbeatPrompt);
            success = true;

            var normalizedResponse = response.Replace("_", "").ToUpperInvariant();
            if (normalizedResponse.Contains(HeartbeatOkToken.Replace("_", "")))
            {
                _logger.LogInformation("Heartbeat: OK (no action needed)");
            }
            else
            {
                _logger.LogInformation("Heartbeat: completed task");
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "Heartbeat execution failed");
        }

        HeartbeatExecuted?.Invoke(this, new HeartbeatEventArgs
        {
            Prompt = HeartbeatPrompt,
            Response = response ?? string.Empty,
            Success = success,
            Error = error
        });

        return response;
    }
}
