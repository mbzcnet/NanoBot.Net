using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent;

public interface ISessionManager
{
    Task<AgentSession> GetOrCreateSessionAsync(string sessionKey, CancellationToken cancellationToken = default);
    Task SaveSessionAsync(AgentSession session, string sessionKey, CancellationToken cancellationToken = default);
    Task ClearSessionAsync(string sessionKey, CancellationToken cancellationToken = default);
    Task InvalidateAsync(string sessionKey);
    IEnumerable<SessionInfo> ListSessions();
}

public record SessionInfo(
    string Key,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    string Path
);

public sealed class SessionManager : ISessionManager
{
    private readonly ChatClientAgent _agent;
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<SessionManager>? _logger;
    private readonly Dictionary<string, AgentSession> _cache;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _sessionsDirectory;

    public SessionManager(
        ChatClientAgent agent,
        IWorkspaceManager workspace,
        ILogger<SessionManager>? logger = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger;
        _cache = new Dictionary<string, AgentSession>();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        _sessionsDirectory = _workspace.GetSessionsPath();

        if (!Directory.Exists(_sessionsDirectory))
        {
            Directory.CreateDirectory(_sessionsDirectory);
        }
    }

    public async Task<AgentSession> GetOrCreateSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(sessionKey, out var cachedSession))
        {
            return cachedSession;
        }

        var session = await LoadSessionAsync(sessionKey, cancellationToken);
        if (session != null)
        {
            _cache[sessionKey] = session;
            return session;
        }

        var newSession = await _agent.CreateSessionAsync(cancellationToken);
        _cache[sessionKey] = newSession;

        _logger?.LogDebug("Created new session for key {SessionKey}", sessionKey);

        return newSession;
    }

    public async Task SaveSessionAsync(AgentSession session, string sessionKey, CancellationToken cancellationToken = default)
    {
        var sessionFile = GetSessionPath(sessionKey);

        try
        {
            var serialized = await _agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
            var json = serialized.GetRawText();

            await File.WriteAllTextAsync(sessionFile, json, cancellationToken);
            _cache[sessionKey] = session;

            _logger?.LogDebug("Saved session {SessionKey} to {SessionFile}", sessionKey, sessionFile);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save session {SessionKey}", sessionKey);
            throw;
        }
    }

    public Task ClearSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var sessionFile = GetSessionPath(sessionKey);

        if (File.Exists(sessionFile))
        {
            File.Delete(sessionFile);
            _logger?.LogDebug("Deleted session file {SessionFile}", sessionFile);
        }

        _cache.Remove(sessionKey);
        _logger?.LogInformation("Cleared session {SessionKey}", sessionKey);

        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string sessionKey)
    {
        _cache.Remove(sessionKey);
        _logger?.LogDebug("Invalidated session cache for {SessionKey}", sessionKey);
        return Task.CompletedTask;
    }

    public IEnumerable<SessionInfo> ListSessions()
    {
        if (!Directory.Exists(_sessionsDirectory))
        {
            return [];
        }

        var sessions = new List<SessionInfo>();

        foreach (var file in Directory.GetFiles(_sessionsDirectory, "*.json"))
        {
            try
            {
                var key = Path.GetFileNameWithoutExtension(file).Replace("_", ":");
                var info = new FileInfo(file);
                sessions.Add(new SessionInfo(
                    Key: key,
                    CreatedAt: info.CreationTimeUtc,
                    UpdatedAt: info.LastWriteTimeUtc,
                    Path: file
                ));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read session file {File}", file);
            }
        }

        return sessions.OrderByDescending(s => s.UpdatedAt);
    }

    private async Task<AgentSession?> LoadSessionAsync(string sessionKey, CancellationToken cancellationToken)
    {
        var sessionFile = GetSessionPath(sessionKey);

        if (!File.Exists(sessionFile))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(sessionFile, cancellationToken);
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);

            var session = await _agent.DeserializeSessionAsync(jsonElement, cancellationToken: cancellationToken);

            _logger?.LogDebug("Loaded session {SessionKey} from {SessionFile}", sessionKey, sessionFile);

            return session;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load session {SessionKey}, creating new one", sessionKey);
            return null;
        }
    }

    private string GetSessionPath(string sessionKey)
    {
        var safeKey = sessionKey.Replace(":", "_").Replace("/", "_").Replace("\\", "_");
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safeKey = safeKey.Replace(c, '_');
        }
        return Path.Combine(_sessionsDirectory, $"{safeKey}.json");
    }
}
