using Microsoft.Extensions.Logging;
using NanoBot.Core.Bus;
using NanoBot.Core.Subagents;
using NanoBot.Core.Workspace;

namespace NanoBot.Infrastructure.Subagents;

public class SubagentManager : ISubagentManager
{
    private readonly IMessageBus _messageBus;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<SubagentManager> _logger;
    private readonly Func<string, string, Task<string>>? _executeSubagent;

    private readonly Dictionary<string, SubagentInfo> _subagents = new();
    private readonly Dictionary<string, CancellationTokenSource> _cancellationTokens = new();
    private readonly object _lock = new();

    public event EventHandler<SubagentCompletedEventArgs>? SubagentCompleted;

    public SubagentManager(
        IMessageBus messageBus,
        IWorkspaceManager workspaceManager,
        ILogger<SubagentManager> logger,
        Func<string, string, Task<string>>? executeSubagent = null)
    {
        _messageBus = messageBus;
        _workspaceManager = workspaceManager;
        _logger = logger;
        _executeSubagent = executeSubagent;
    }

    public async Task<SubagentResult> SpawnAsync(
        string task,
        string? label,
        string originChannel,
        string originChatId,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var displayLabel = label ?? (task.Length > 30 ? task[..30] + "..." : task);

        var info = new SubagentInfo
        {
            Id = id,
            Task = task,
            Label = displayLabel,
            OriginChannel = originChannel,
            OriginChatId = originChatId,
            Status = SubagentStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };

        lock (_lock)
        {
            _subagents[id] = info;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_lock)
        {
            _cancellationTokens[id] = cts;
        }

        _logger.LogInformation("Spawned subagent [{Id}]: {Label}", id, displayLabel);

        try
        {
            var systemPrompt = BuildSubagentPrompt(task);
            var output = await RunSubagentAsync(id, task, systemPrompt, cts.Token);

            lock (_lock)
            {
                info.Status = SubagentStatus.Completed;
                info.CompletedAt = DateTimeOffset.UtcNow;
            }

            var result = new SubagentResult
            {
                Id = id,
                Status = SubagentStatus.Completed,
                Output = output,
                Duration = DateTimeOffset.UtcNow - info.StartedAt
            };

            await AnnounceResultAsync(result, originChannel, originChatId);

            SubagentCompleted?.Invoke(this, new SubagentCompletedEventArgs
            {
                Result = result,
                OriginChannel = originChannel,
                OriginChatId = originChatId
            });

            _logger.LogInformation("Subagent [{Id}] completed successfully", id);
            return result;
        }
        catch (OperationCanceledException)
        {
            lock (_lock)
            {
                info.Status = SubagentStatus.Cancelled;
                info.CompletedAt = DateTimeOffset.UtcNow;
            }

            var result = new SubagentResult
            {
                Id = id,
                Status = SubagentStatus.Cancelled,
                Duration = DateTimeOffset.UtcNow - info.StartedAt
            };

            _logger.LogInformation("Subagent [{Id}] cancelled", id);
            return result;
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                info.Status = SubagentStatus.Failed;
                info.CompletedAt = DateTimeOffset.UtcNow;
            }

            var result = new SubagentResult
            {
                Id = id,
                Status = SubagentStatus.Failed,
                Error = ex.Message,
                Duration = DateTimeOffset.UtcNow - info.StartedAt
            };

            await AnnounceResultAsync(result, originChannel, originChatId);

            SubagentCompleted?.Invoke(this, new SubagentCompletedEventArgs
            {
                Result = result,
                OriginChannel = originChannel,
                OriginChatId = originChatId
            });

            _logger.LogError(ex, "Subagent [{Id}] failed", id);
            return result;
        }
        finally
        {
            lock (_lock)
            {
                _cancellationTokens.Remove(id);
            }
        }
    }

    public IReadOnlyList<SubagentInfo> GetActiveSubagents()
    {
        lock (_lock)
        {
            return _subagents.Values
                .Where(s => s.Status == SubagentStatus.Running)
                .ToList()
                .AsReadOnly();
        }
    }

    public SubagentInfo? GetSubagent(string id)
    {
        lock (_lock)
        {
            return _subagents.GetValueOrDefault(id);
        }
    }

    public bool Cancel(string id)
    {
        lock (_lock)
        {
            if (_cancellationTokens.TryGetValue(id, out var cts))
            {
                cts.Cancel();
                return true;
            }
            return false;
        }
    }

    private async Task<string> RunSubagentAsync(string id, string task, string systemPrompt, CancellationToken cancellationToken)
    {
        if (_executeSubagent != null)
        {
            return await _executeSubagent(systemPrompt, task);
        }

        await Task.Delay(100, cancellationToken);
        return "Subagent execution not configured. Please provide an execute callback.";
    }

    private string BuildSubagentPrompt(string task)
    {
        var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm (dddd)");
        var tz = TimeZoneInfo.Local.Id;
        var workspacePath = _workspaceManager.GetWorkspacePath();
        var skillsPath = _workspaceManager.GetSkillsPath();

        return $"""
# Subagent

## Current Time
{now} ({tz})

You are a subagent spawned by the main agent to complete a specific task.

## Rules
1. Stay focused - complete only the assigned task, nothing else
2. Your final response will be reported back to the main agent
3. Do not initiate conversations or take on side tasks
4. Be concise but informative in your findings

## What You Can Do
- Read and write files in the workspace
- Execute shell commands
- Search the web and fetch web pages
- Complete the task thoroughly

## What You Cannot Do
- Send messages directly to users (no message tool available)
- Spawn other subagents
- Access the main agent's conversation history

## Workspace
Your workspace is at: {workspacePath}
Skills are available at: {skillsPath} (read SKILL.md files as needed)

When you have completed the task, provide a clear summary of your findings or actions.
""";
    }

    private async Task AnnounceResultAsync(SubagentResult result, string originChannel, string originChatId)
    {
        var statusText = result.Status == SubagentStatus.Completed ? "completed successfully" : "failed";
        var taskDesc = GetSubagent(result.Id)?.Task ?? "Unknown";
        var output = result.Output ?? result.Error ?? "No output";

        var announceContent = $"""
[Subagent '{result.Id}' {statusText}]

Task: {taskDesc}

Result:
{output}

Summarize this naturally for the user. Keep it brief (1-2 sentences). Do not mention technical details like "subagent" or task IDs.
""";

        var message = new InboundMessage
        {
            Channel = "system",
            SenderId = "subagent",
            ChatId = $"{originChannel}:{originChatId}",
            Content = announceContent,
            Timestamp = DateTimeOffset.UtcNow
        };

        await _messageBus.PublishInboundAsync(message);
        _logger.LogDebug("Subagent [{Id}] announced result to {Channel}:{ChatId}", result.Id, originChannel, originChatId);
    }
}
