using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Heartbeat;
using NanoBot.Core.Workspace;

namespace NanoBot.Infrastructure.Heartbeat;

public class HeartbeatService : IHeartbeatService, IDisposable
{
    private const int DefaultIntervalSeconds = 30 * 60;

    private const string DecideSystemPrompt = "You are a heartbeat agent. Call the heartbeat tool to report your decision.";

    private readonly IWorkspaceManager _workspaceManager;
    private readonly IChatClient? _chatClient;
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
        IChatClient? chatClient,
        ILogger<HeartbeatService> logger,
        Func<string, Task<string>>? onHeartbeat = null,
        int intervalSeconds = DefaultIntervalSeconds,
        bool enabled = true)
    {
        _workspaceManager = workspaceManager;
        _chatClient = chatClient;
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
        string? response = null;
        string? error = null;
        bool success = false;

        try
        {
            var content = await ReadHeartbeatFileAsync();
            if (IsHeartbeatEmpty(content))
            {
                _logger.LogDebug("Heartbeat: no tasks (HEARTBEAT.md empty)");
                return null;
            }

            var (action, tasks) = await DecideAsync(content!, CancellationToken.None);

            if (!string.Equals(action, "run", StringComparison.OrdinalIgnoreCase))
            {
                response = "skip";
                success = true;
                _logger.LogInformation("Heartbeat: OK (nothing to report)");
            }
            else
            {
                if (_onHeartbeat == null)
                {
                    response = tasks;
                    success = true;
                    _logger.LogWarning("Heartbeat: tasks found but onHeartbeat callback not provided");
                }
                else
                {
                    response = await _onHeartbeat(tasks);
                    success = true;
                    _logger.LogInformation("Heartbeat: completed task");
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "Heartbeat execution failed");
        }

        HeartbeatExecuted?.Invoke(this, new HeartbeatEventArgs
        {
            Prompt = "HEARTBEAT",
            Response = response ?? string.Empty,
            Success = success,
            Error = error
        });

        return response;
    }

    private async Task<(string Action, string Tasks)> DecideAsync(string heartbeatMarkdown, CancellationToken cancellationToken)
    {
        if (_chatClient == null)
        {
            return ("skip", string.Empty);
        }

        var heartbeatTool = AIFunctionFactory.Create(
            (string action, string? tasks) => "",
            new AIFunctionFactoryOptions
            {
                Name = "heartbeat",
                Description = "Report heartbeat decision after reviewing tasks. action=skip|run; tasks is required for run."
            });

        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, DecideSystemPrompt),
                new ChatMessage(ChatRole.User, $"Review the following HEARTBEAT.md and decide whether there are active tasks.\n\n{heartbeatMarkdown}")
            ],
            options: new ChatOptions { Tools = [heartbeatTool] },
            cancellationToken: cancellationToken);

        var call = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .FirstOrDefault(fc => string.Equals(fc.Name, "heartbeat", StringComparison.OrdinalIgnoreCase));

        if (call?.Arguments is not IDictionary<string, object?> args)
        {
            return ("skip", string.Empty);
        }

        var action = args.TryGetValue("action", out var a) ? a?.ToString() ?? "skip" : "skip";
        var tasks = args.TryGetValue("tasks", out var t) ? t?.ToString() ?? string.Empty : string.Empty;
        return (action, tasks);
    }
}
