using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NanoBot.Core.Workspace;

namespace NanoBot.Core.Debug;

/// <summary>
/// Manages debug state for sessions and writes debug logs to session directories.
/// </summary>
public interface IDebugState
{
    bool IsDebugEnabled(string sessionKey);
    void EnableDebug(string sessionKey);
    void DisableDebug(string sessionKey);
    Task LogAsync(string sessionKey, string phase, object? data, CancellationToken cancellationToken = default);
    Task<int> StartRequestLogAsync(string sessionKey, CancellationToken cancellationToken = default);
    Task AppendToLogAsync(string sessionKey, int requestId, string content, CancellationToken cancellationToken = default);
    Task FinishRequestLogAsync(string sessionKey, int requestId, string? reason = null, CancellationToken cancellationToken = default);
    void FinishRequestLogSync(string sessionKey, int requestId, string? reason = null);
}

public sealed class DebugState : IDebugState
{
    private readonly ConcurrentDictionary<string, bool> _debugSessions = new();
    private readonly IWorkspaceManager _workspace;
    private readonly string _logsDirectoryName = "logs";
    private readonly ConcurrentDictionary<string, int> _requestCounters = new();
    private readonly ConcurrentDictionary<string, HashSet<int>> _activeRequests = new();

    public DebugState(IWorkspaceManager workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        // Register process exit handler to ensure logs are flushed
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        // Register POSIX signal handlers for Unix/Linux
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnPosixSignal);
                PosixSignalRegistration.Create(PosixSignal.SIGINT, OnPosixSignal);
            }
            catch
            {
                // Ignore if POSIX signals are not supported
            }
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        FlushAllActiveLogs("Process exited");
    }

    private void OnPosixSignal(PosixSignalContext context)
    {
        FlushAllActiveLogs($"Signal received: {context.Signal}");
    }

    private void FlushAllActiveLogs(string reason)
    {
        // Flush all active request logs on process exit
        foreach (var kvp in _activeRequests)
        {
            var sessionKey = kvp.Key;
            foreach (var requestId in kvp.Value.ToArray())
            {
                try
                {
                    FinishRequestLogSync(sessionKey, requestId, reason);
                }
                catch
                {
                    // Ignore errors during process exit
                }
            }
        }
    }

    public bool IsDebugEnabled(string sessionKey)
    {
        return _debugSessions.TryGetValue(sessionKey, out var enabled) && enabled;
    }

    public void EnableDebug(string sessionKey)
    {
        _debugSessions[sessionKey] = true;
    }

    public void DisableDebug(string sessionKey)
    {
        _debugSessions.TryRemove(sessionKey, out _);
    }

    public async Task LogAsync(string sessionKey, string phase, object? data, CancellationToken cancellationToken = default)
    {
        if (!IsDebugEnabled(sessionKey))
            return;

        try
        {
            var logDir = GetLogDirectory(sessionKey);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var logFile = Path.Combine(logDir, $"debug_{timestamp}.jsonl");

            var json = JsonSerializer.Serialize(new
            {
                timestamp = DateTime.UtcNow,
                phase,
                data
            });

            await File.AppendAllTextAsync(logFile, json + Environment.NewLine, cancellationToken);
        }
        catch
        {
            // Silently ignore logging failures
        }
    }

    public async Task<int> StartRequestLogAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        if (!IsDebugEnabled(sessionKey))
            return -1;

        try
        {
            var logDir = GetLogDirectory(sessionKey);
            var requestId = _requestCounters.AddOrUpdate(sessionKey, 1, (_, current) => current + 1);
            var logFile = Path.Combine(logDir, $"debug_{requestId:D3}.md");

            var header = $"# Debug Log - Request #{requestId:D3}\n" +
                        $"## Timestamp\n" +
                        $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC\n\n" +
                        "## IN - LLM Request\n\n";

            await File.WriteAllTextAsync(logFile, header, cancellationToken);

            // Track this request as active
            _activeRequests.AddOrUpdate(sessionKey, [requestId], (_, set) =>
            {
                set.Add(requestId);
                return set;
            });

            return requestId;
        }
        catch
        {
            return -1;
        }
    }

    public async Task AppendToLogAsync(string sessionKey, int requestId, string content, CancellationToken cancellationToken = default)
    {
        if (!IsDebugEnabled(sessionKey) || requestId < 0)
            return;

        try
        {
            var logDir = GetLogDirectory(sessionKey);
            var logFile = Path.Combine(logDir, $"debug_{requestId:D3}.md");
            await File.AppendAllTextAsync(logFile, content, cancellationToken);
        }
        catch
        {
            // Silently ignore
        }
    }

    public async Task FinishRequestLogAsync(string sessionKey, int requestId, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (!IsDebugEnabled(sessionKey) || requestId < 0)
            return;

        try
        {
            var logDir = GetLogDirectory(sessionKey);
            var logFile = Path.Combine(logDir, $"debug_{requestId:D3}.md");
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## [END] Request Completed");
            sb.AppendLine();
            sb.AppendLine($"- **Timestamp**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC");
            if (!string.IsNullOrEmpty(reason))
            {
                sb.AppendLine($"- **Reason**: {reason}");
            }
            sb.AppendLine("- **Status**: LLM response stream finished normally");
            sb.AppendLine();
            await File.AppendAllTextAsync(logFile, sb.ToString(), cancellationToken);

            // Remove from active requests
            RemoveActiveRequest(sessionKey, requestId);
        }
        catch
        {
            // Silently ignore
        }
    }

    public void FinishRequestLogSync(string sessionKey, int requestId, string? reason = null)
    {
        if (requestId < 0)
            return;

        try
        {
            var logDir = GetLogDirectory(sessionKey);
            var logFile = Path.Combine(logDir, $"debug_{requestId:D3}.md");

            // Check if [END] already exists to avoid duplicates
            if (File.Exists(logFile))
            {
                var content = File.ReadAllText(logFile);
                if (content.Contains("## [END] Request Completed"))
                {
                    RemoveActiveRequest(sessionKey, requestId);
                    return;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## [END] Request Completed");
            sb.AppendLine();
            sb.AppendLine($"- **Timestamp**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC");
            if (!string.IsNullOrEmpty(reason))
            {
                sb.AppendLine($"- **Reason**: {reason}");
            }
            sb.AppendLine("- **Status**: LLM response stream finished normally");
            sb.AppendLine();
            File.AppendAllText(logFile, sb.ToString());

            // Remove from active requests
            RemoveActiveRequest(sessionKey, requestId);
        }
        catch
        {
            // Silently ignore
        }
    }

    private void RemoveActiveRequest(string sessionKey, int requestId)
    {
        if (_activeRequests.TryGetValue(sessionKey, out var requests))
        {
            requests.Remove(requestId);
            if (requests.Count == 0)
            {
                _activeRequests.TryRemove(sessionKey, out _);
            }
        }
    }

    private string GetLogDirectory(string sessionKey)
    {
        var sessionsPath = _workspace.GetSessionsPath();
        var sessionPath = Path.Combine(sessionsPath, sessionKey);
        var logDir = Path.Combine(sessionPath, _logsDirectoryName);
        if (!Directory.Exists(logDir))
            Directory.CreateDirectory(logDir);
        return logDir;
    }
}
