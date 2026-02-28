using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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

    int GetLastConsolidated(string sessionKey);

    void SetLastConsolidated(string sessionKey, int lastConsolidated);
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

    private readonly string _legacySessionsDirectory;

    private readonly Dictionary<string, int> _lastConsolidatedBySessionKey;

    public SessionManager(
        ChatClientAgent agent,
        IWorkspaceManager workspace,
        ILogger<SessionManager>? logger = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger;
        _cache = new Dictionary<string, AgentSession>();
        _lastConsolidatedBySessionKey = new Dictionary<string, int>(StringComparer.Ordinal);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _sessionsDirectory = _workspace.GetSessionsPath();

        _legacySessionsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nanobot",
            "sessions");

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

    public int GetLastConsolidated(string sessionKey)
    {
        return _lastConsolidatedBySessionKey.GetValueOrDefault(sessionKey, 0);
    }

    public void SetLastConsolidated(string sessionKey, int lastConsolidated)
    {
        if (lastConsolidated < 0)
        {
            lastConsolidated = 0;
        }

        _lastConsolidatedBySessionKey[sessionKey] = lastConsolidated;
    }

    public async Task SaveSessionAsync(AgentSession session, string sessionKey, CancellationToken cancellationToken = default)
    {
        var sessionFile = GetSessionPath(sessionKey);

        try
        {
            var serializedSession = await _agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
            var allMessages = GetAllMessages(session);

            var metadata = await BuildMetadataLineAsync(sessionKey, sessionFile, serializedSession, cancellationToken);

            await using var fs = new FileStream(sessionFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(fs);

            await writer.WriteLineAsync(metadata);

            foreach (var m in allMessages)
            {
                await writer.WriteLineAsync(m.ToJsonString(_jsonOptions));
            }

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

        foreach (var file in Directory.GetFiles(_sessionsDirectory, "*.jsonl"))
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

        await TryMigrateLegacySessionAsync(sessionKey, sessionFile, cancellationToken);

        if (!File.Exists(sessionFile))
        {
            return null;
        }

        try
        {
            await using var fs = new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);

            var firstLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return null;
            }

            JsonObject? metadataLine;
            try
            {
                metadataLine = JsonSerializer.Deserialize<JsonObject>(firstLine, _jsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }

            if (metadataLine == null)
            {
                return null;
            }

            if (metadataLine.TryGetPropertyValue("metadata", out var metaNode) && metaNode is JsonObject metaObj &&
                metaObj.TryGetPropertyValue("agent_session", out var agentSessionNode) && agentSessionNode != null)
            {
                if (metaObj.TryGetPropertyValue("last_consolidated", out var lcNode) && lcNode != null)
                {
                    if (lcNode.GetValueKind() == JsonValueKind.Number && lcNode.AsValue().TryGetValue<int>(out var lc))
                    {
                        SetLastConsolidated(sessionKey, lc);
                    }
                }

                var raw = agentSessionNode.ToJsonString(_jsonOptions);
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(raw, _jsonOptions);
                var session = await _agent.DeserializeSessionAsync(jsonElement, cancellationToken: cancellationToken);

                _logger?.LogDebug("Loaded session {SessionKey} from {SessionFile}", sessionKey, sessionFile);
                return session;
            }

            return null;
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
        return Path.Combine(_sessionsDirectory, $"{safeKey}.jsonl");
    }

    private string GetLegacySessionPath(string sessionKey)
    {
        var safeKey = sessionKey.Replace(":", "_").Replace("/", "_").Replace("\\", "_");
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safeKey = safeKey.Replace(c, '_');
        }
        return Path.Combine(_legacySessionsDirectory, $"{safeKey}.jsonl");
    }

    private async Task TryMigrateLegacySessionAsync(string sessionKey, string sessionFile, CancellationToken cancellationToken)
    {
        if (File.Exists(sessionFile))
        {
            return;
        }

        var legacyPath = GetLegacySessionPath(sessionKey);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(sessionFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.Move(legacyPath, sessionFile);
            _logger?.LogInformation("Migrated session {SessionKey} from legacy path {LegacyPath}", sessionKey, legacyPath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to migrate legacy session {SessionKey} from {LegacyPath}", sessionKey, legacyPath);
        }

        await Task.CompletedTask;
    }

    private sealed record JsonlSessionMessage(
        string Role,
        string Content,
        string Timestamp,
        JsonArray? ToolCalls = null,
        string? ToolCallId = null,
        string? Name = null);

    private List<JsonObject> GetAllMessages(AgentSession session)
    {
        var messages = new List<ChatMessage>();

        var historyProvider = session.GetService<ChatHistoryProvider>();
        if (historyProvider != null)
        {
            var method = typeof(ChatHistoryProvider).GetMethod(
                "GetAllMessages",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                var result = method.Invoke(historyProvider, null);
                if (result is IEnumerable<ChatMessage> enumerable)
                {
                    messages.AddRange(enumerable);
                }
            }
        }

        return messages.Select(SerializeMessage).ToList();
    }

    private async Task<string> BuildMetadataLineAsync(
        string sessionKey,
        string sessionFile,
        JsonElement serializedSession,
        CancellationToken cancellationToken)
    {
        var createdAt = DateTimeOffset.Now;

        if (File.Exists(sessionFile))
        {
            try
            {
                await using var fs = new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                var firstLine = await reader.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(firstLine))
                {
                    var existing = JsonSerializer.Deserialize<JsonObject>(firstLine, _jsonOptions);
                    if (existing != null &&
                        existing.TryGetPropertyValue("created_at", out var createdNode) &&
                        createdNode != null &&
                        DateTimeOffset.TryParse(createdNode.GetValue<string>(), out var parsed))
                    {
                        createdAt = parsed;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        var lastConsolidated = GetLastConsolidated(sessionKey);

        var metaObj = new JsonObject
        {
            ["agent_session"] = JsonNode.Parse(serializedSession.GetRawText())
        };

        var metadataLine = new JsonObject
        {
            ["_type"] = "metadata",
            ["key"] = sessionKey,
            ["created_at"] = createdAt.ToString("o"),
            ["updated_at"] = DateTimeOffset.Now.ToString("o"),
            ["metadata"] = metaObj,
            ["last_consolidated"] = lastConsolidated
        };

        return metadataLine.ToJsonString(_jsonOptions);
    }

    private JsonObject SerializeMessage(ChatMessage message)
    {
        var timestamp = DateTimeOffset.Now.ToString("o");
        var role = message.Role.ToString().ToLowerInvariant();
        var content = message.Text ?? string.Empty;

        JsonArray? toolCalls = null;
        string? toolCallId = null;
        string? name = null;

        foreach (var c in message.Contents)
        {
            if (c is FunctionCallContent fcc)
            {
                toolCalls ??= new JsonArray();

                var argsDict = fcc.Arguments as IDictionary<string, object?>;
                var argsJson = argsDict != null ? JsonSerializer.Serialize(argsDict, _jsonOptions) : "{}";

                var functionObj = new JsonObject
                {
                    ["name"] = fcc.Name,
                    // arguments 需要可 grep 且与原项目一致地保持 JSON 字符串
                    ["arguments"] = argsJson
                };

                var toolCallObj = new JsonObject
                {
                    ["id"] = fcc.CallId,
                    ["type"] = "function",
                    ["function"] = functionObj
                };

                toolCalls.Add(toolCallObj);

                // 与 OpenAI 格式/原项目兼容：tool call 本身也可能需要 name
                name ??= fcc.Name;
            }
            else if (c is FunctionResultContent frc)
            {
                toolCallId ??= frc.CallId;
                // FunctionResultContent 没有 Name 属性，使用空字符串
            }
        }

        var obj = new JsonObject
        {
            ["role"] = role,
            ["content"] = content,
            ["timestamp"] = timestamp
        };

        if (toolCalls != null)
        {
            obj["tool_calls"] = toolCalls;
        }

        if (!string.IsNullOrEmpty(toolCallId))
        {
            obj["tool_call_id"] = toolCallId;
        }

        if (!string.IsNullOrEmpty(name))
        {
            obj["name"] = name;
        }

        return obj;
    }
}
