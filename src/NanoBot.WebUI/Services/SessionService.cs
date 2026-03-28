using System.Text.Json;
using System.IO;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Sessions;
using NanoBot.Core.Storage;
using NanoBot.Core.Workspace;
using AgentSessionManager = NanoBot.Agent.ISessionManager;

namespace NanoBot.WebUI.Services;

public class SessionService : ISessionService
{
    private readonly ILogger<SessionService> _logger;
    private readonly AgentSessionManager _sessionManager;
    private readonly IWorkspaceManager _workspace;
    private readonly IFileStorageService _fileStorage;
    private readonly SessionMessageParser _parser;

    public SessionService(
        ILogger<SessionService> logger,
        AgentSessionManager sessionManager,
        IWorkspaceManager workspace,
        IFileStorageService fileStorage,
        SessionMessageParser parser)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _workspace = workspace;
        _fileStorage = fileStorage;
        _parser = parser;
    }

    public Task<List<SessionInfo>> GetSessionsAsync()
    {
        try
        {
            var allSessions = _sessionManager.ListSessions().ToList();

            var sessions = allSessions
                .Where(s => s.Key.StartsWith("chat:") || s.Key.StartsWith("chat_"))
                .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt ?? DateTimeOffset.MinValue)
                .Select(s => new SessionInfo
                {
                    Id = s.Key.Replace("chat:", "").Replace("chat_", ""),
                    Title = s.Title ?? GenerateDefaultTitle(s.Key),
                    CreatedAt = (s.CreatedAt ?? DateTimeOffset.Now).DateTime,
                    UpdatedAt = (s.UpdatedAt ?? DateTimeOffset.Now).DateTime,
                    ProfileId = s.ProfileId
                })
                .ToList();

            return Task.FromResult(sessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing sessions");
            return Task.FromResult(new List<SessionInfo>());
        }
    }

    public async Task<SessionInfo?> GetSessionAsync(string sessionId)
    {
        try
        {
            var sessionKeyWithUnderscore = $"chat_{sessionId}";
            var sessionKeyWithColon = $"chat:{sessionId}";
            var agentSession = _sessionManager.ListSessions()
                .FirstOrDefault(s => s.Key == sessionKeyWithUnderscore || s.Key == sessionKeyWithColon);

            if (agentSession == null)
                return null;

            return new SessionInfo
            {
                Id = sessionId,
                Title = agentSession.Title ?? GenerateDefaultTitle(agentSession.Key),
                CreatedAt = (agentSession.CreatedAt ?? DateTimeOffset.Now).DateTime,
                UpdatedAt = (agentSession.UpdatedAt ?? DateTimeOffset.Now).DateTime,
                ProfileId = agentSession.ProfileId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<SessionInfo> CreateSessionAsync(string? title = null, string? profileId = null)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var sessionKey = $"chat_{sessionId}";

        try
        {
            var agentSession = await _sessionManager.GetOrCreateSessionAsync(sessionKey);

            var now = DateTime.Now;
            var sessionTitle = title ?? $"会话 {now:MM-dd HH:mm}";

            _sessionManager.SetSessionTitle(sessionKey, sessionTitle);
            if (!string.IsNullOrEmpty(profileId))
                _sessionManager.SetSessionProfileId(sessionKey, profileId);

            await _sessionManager.SaveSessionAsync(agentSession, sessionKey);

            var session = new SessionInfo
            {
                Id = sessionId,
                Title = sessionTitle,
                CreatedAt = now,
                UpdatedAt = now,
                ProfileId = profileId
            };

            _logger.LogInformation("Created new session: {SessionId}", sessionId);
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session");
            throw;
        }
    }

    public async Task RenameSessionAsync(string sessionId, string newTitle)
    {
        try
        {
            var sessionKey = $"chat_{sessionId}";
            var agentSession = await _sessionManager.GetOrCreateSessionAsync(sessionKey);
            _sessionManager.SetSessionTitle(sessionKey, newTitle);
            await _sessionManager.SaveSessionAsync(agentSession, sessionKey);
            _logger.LogInformation("Renamed session {SessionId} to {NewTitle}", sessionId, newTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renaming session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task SetSessionProfileAsync(string sessionId, string profileId)
    {
        try
        {
            var sessionKey = $"chat_{sessionId}";
            var agentSession = await _sessionManager.GetOrCreateSessionAsync(sessionKey);
            _sessionManager.SetSessionProfileId(sessionKey, profileId);
            await _sessionManager.SaveSessionAsync(agentSession, sessionKey);
            _logger.LogInformation("Set session profile {ProfileId} for session {SessionId}", profileId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting profile {ProfileId} for session {SessionId}", profileId, sessionId);
            throw;
        }
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        try
        {
            var sessionKey = $"chat_{sessionId}";
            await _sessionManager.ClearSessionAsync(sessionKey);
            await _fileStorage.DeleteSessionDirectoryAsync(sessionId);
            _logger.LogInformation("Deleted session: {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting session {SessionId}", sessionId);
            throw;
        }
    }

    public Task<List<MessageInfo>> GetMessagesAsync(string sessionId)
        => _parser.ParseMessagesAsync(sessionId);

    public Task<MessageInfo> AddMessageAsync(string sessionId, string role, string content, List<AttachmentInfo>? attachments = null)
    {
        try
        {
            var now = DateTime.Now;
            var messageId = Guid.NewGuid().ToString("N");

            var message = new MessageInfo
            {
                Id = messageId,
                SessionId = sessionId,
                Role = role,
                Content = content,
                Timestamp = now,
                Attachments = attachments ?? new List<AttachmentInfo>()
            };

            _logger.LogInformation("Message created for session {SessionId}: {Role}", sessionId, role);
            return Task.FromResult(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding message for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task DeleteMessagesFromAsync(string sessionId, int fromIndex)
    {
        try
        {
            var sessionKey = $"chat_{sessionId}";
            var sessionsPath = _workspace.GetSessionsPath();
            var sessionFile = Path.Combine(sessionsPath, $"chat_{sessionId}.jsonl");

            if (!File.Exists(sessionFile))
            {
                _logger.LogWarning("Session file not found for {SessionId}", sessionId);
                return;
            }

            var allLines = await File.ReadAllLinesAsync(sessionFile);
            if (allLines.Length == 0)
                return;

            // First line is metadata
            var metadataLine = allLines[0];
            var messageLines = allLines.Skip(1).ToList();

            // Filter out metadata lines
            var actualMessageLines = messageLines.Where(line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return false;
                try
                {
                    var doc = JsonSerializer.Deserialize<JsonElement>(line);
                    if (doc.TryGetProperty("_type", out var typeElement))
                        return typeElement.GetString() != "metadata";
                    return true;
                }
                catch { return false; }
            }).ToList();

            if (fromIndex < 0 || fromIndex >= actualMessageLines.Count)
            {
                _logger.LogWarning("Invalid message index {FromIndex} for session {SessionId}, total messages: {Count}",
                    fromIndex, sessionId, actualMessageLines.Count);
                return;
            }

            var linesToKeep = actualMessageLines.Take(fromIndex).ToList();

            var newLines = new List<string> { metadataLine };
            newLines.AddRange(linesToKeep);

            await File.WriteAllLinesAsync(sessionFile, newLines);
            await _sessionManager.InvalidateAsync(sessionKey);

            _logger.LogInformation("Deleted messages from index {FromIndex} for session {SessionId}, remaining messages: {Count}",
                fromIndex, sessionId, linesToKeep.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting messages from index {FromIndex} for session {SessionId}", fromIndex, sessionId);
            throw;
        }
    }

    private static string GenerateDefaultTitle(string sessionKey)
    {
        var sessionId = sessionKey.Replace("chat_", "").Replace("chat:", "");
        return $"会话 {sessionId.Substring(0, Math.Min(8, sessionId.Length))}";
    }
}
