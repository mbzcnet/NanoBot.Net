using NanoBot.Agent;
using NanoBot.Core.Sessions;
using NanoBot.Core.Storage;
using NanoBot.Core.Workspace;
using System.Text.Json;

namespace NanoBot.WebUI.Services;

public class SessionService : ISessionService
{
    private readonly ILogger<SessionService> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly IWorkspaceManager _workspace;
    private readonly IFileStorageService _fileStorage;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SessionService(
        ILogger<SessionService> logger,
        ISessionManager sessionManager,
        IWorkspaceManager workspace,
        IFileStorageService fileStorage)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _workspace = workspace;
        _fileStorage = fileStorage;
    }

    public Task<List<SessionInfo>> GetSessionsAsync()
    {
        try
        {
            var sessions = _sessionManager.ListSessions()
                .Where(s => s.Key.StartsWith("webui:"))
                .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt ?? DateTimeOffset.MinValue)
                .Select(s => new SessionInfo
                {
                    Id = s.Key.Replace("webui:", ""),
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
            var sessionKey = $"webui:{sessionId}";
            var agentSession = _sessionManager.ListSessions()
                .FirstOrDefault(s => s.Key == sessionKey);

            if (agentSession == null)
                return null;

            return new SessionInfo
            {
                Id = sessionId,
                Title = agentSession.Title ?? GenerateDefaultTitle(sessionKey),
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
        var sessionKey = $"webui:{sessionId}";

        try
        {
            var agentSession = await _sessionManager.GetOrCreateSessionAsync(sessionKey);

            var now = DateTime.Now;
            var sessionTitle = title ?? $"会话 {now:MM-dd HH:mm}";

            _sessionManager.SetSessionTitle(sessionKey, sessionTitle);
            if (!string.IsNullOrEmpty(profileId))
            {
                _sessionManager.SetSessionProfileId(sessionKey, profileId);
            }

            // 立即保存会话到文件，确保 ListSessions 可以读取到
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
            var sessionKey = $"webui:{sessionId}";
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
            var sessionKey = $"webui:{sessionId}";
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
            var sessionKey = $"webui:{sessionId}";
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

    public async Task<List<MessageInfo>> GetMessagesAsync(string sessionId)
    {
        try
        {
            var sessionKey = $"webui:{sessionId}";
            await _sessionManager.GetOrCreateSessionAsync(sessionKey);

            var sessionsPath = _workspace.GetSessionsPath();
            var sessionFile = Path.Combine(sessionsPath, $"{sessionKey.Replace(":", "_")}.jsonl");

            if (!File.Exists(sessionFile))
                return new List<MessageInfo>();

            var messagesList = new List<MessageInfo>();
            var lines = await File.ReadAllLinesAsync(sessionFile);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var msg = JsonSerializer.Deserialize<JsonElement>(line);
                    
                    // 跳过 metadata 行
                    if (msg.TryGetProperty("_type", out var typeElement) && typeElement.GetString() == "metadata")
                        continue;

                    string role = "user";
                    string content = string.Empty;
                    
                    // 解析消息 - 支持两种格式
                    // 格式1: { "role": "user", "content": "text" }
                    // 格式2: { "role": "user", "contents": [{"$type": "text", "text": "..."}] }
                    
                    if (msg.TryGetProperty("role", out var roleElement))
                    {
                        role = roleElement.GetString()?.ToLower() ?? "user";
                        
                        if (msg.TryGetProperty("content", out var contentElement))
                    {
                        // 简单格式：content 是字符串
                        if (contentElement.ValueKind == JsonValueKind.String)
                        {
                            content = contentElement.GetString() ?? string.Empty;
                        }
                        // 复杂格式：contents 是数组（目前 SessionManager 没有使用这种格式，但保留兼容）
                        else if (contentElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in contentElement.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                     content += item.GetString();
                                }
                                else if (item.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                                {
                                    content += textElement.GetString() ?? string.Empty;
                                }
                            }
                        }
                    }

                    // 增加对 tool_calls 的解析，以便在 UI 中显示工具调用参数
                    if (msg.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var call in toolCallsElement.EnumerateArray())
                        {
                            if (call.TryGetProperty("function", out var funcElement))
                            {
                                var funcName = funcElement.GetProperty("name").GetString();
                                var funcArgs = funcElement.GetProperty("arguments").GetString();
                                
                                // 将工具调用参数格式化为 markdown 代码块追加到 content
                                if (!string.IsNullOrEmpty(funcName))
                                {
                                    if (!string.IsNullOrEmpty(content)) content += "\n\n";
                                    content += $"> **Tool Call**: `{funcName}`\n```json\n{funcArgs}\n```";
                                }
                            }
                        }
                    }
                    
                    // 增加对 tool_call_id (FunctionResult) 的特殊处理
                    // 如果是 tool 角色，且 content 是 JSON 格式的结果，尝试美化显示或提取关键信息
                    if (role == "tool" && !string.IsNullOrWhiteSpace(content))
                    {
                        // 尝试解析 JSON 结果
                        try 
                        {
                             // 简单的 JSON 格式化，如果已经是 JSON 字符串
                             if (content.TrimStart().StartsWith("{"))
                             {
                                 var resultObj = JsonSerializer.Deserialize<JsonElement>(content);
                                 
                                 // 如果是 snapshot 结果，提取 imagePath 并追加图片链接
                                 // 注意：这里只是为了让 Tool 消息本身更好看。AgentRuntime 会在 Assistant 消息中注入图片。
                                 // 但如果 AgentRuntime 失败，或者用户想直接看 Tool 结果，这里是一个补救。
                                 
                                 // 无论是否 snapshot，都尝试格式化 JSON 以便阅读
                                 content = $"```json\n{JsonSerializer.Serialize(resultObj, new JsonSerializerOptions { WriteIndented = true })}\n```";
                             }
                        }
                        catch { /* 忽略解析错误，保持原样 */ }
                    }
                    
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        messagesList.Add(new MessageInfo
                        {
                            Id = $"{sessionId}_{messagesList.Count}",
                            SessionId = sessionId,
                            Role = role,
                            Content = content,
                            Timestamp = DateTime.Now
                        });
                    }
                }
                }
                catch
                {
                }
            }

            return messagesList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for session {SessionId}", sessionId);
            return new List<MessageInfo>();
        }
    }

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

    private string GenerateDefaultTitle(string sessionKey)
    {
        var sessionId = sessionKey.Replace("webui:", "");
        return $"会话 {sessionId.Substring(0, Math.Min(8, sessionId.Length))}";
    }
}
