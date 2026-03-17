using System.Collections.Concurrent;
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
}

public sealed class DebugState : IDebugState
{
    private readonly ConcurrentDictionary<string, bool> _debugSessions = new();
    private readonly IWorkspaceManager _workspace;
    private readonly string _logsDirectoryName = "logs";
    private readonly ConcurrentDictionary<string, int> _requestCounters = new();

    public DebugState(IWorkspaceManager workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
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
