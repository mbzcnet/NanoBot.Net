using NanoBot.Agent;
using NanoBot.Core.Workspace;

namespace NanoBot.WebUI.Services;

public class SessionService : ISessionService
{
    private readonly ILogger<SessionService> _logger;
    private readonly ISessionManager _agentSessionManager;
    private readonly IWorkspaceManager _workspace;

    public SessionService(
        ILogger<SessionService> logger,
        ISessionManager agentSessionManager,
        IWorkspaceManager workspace)
    {
        _logger = logger;
        _agentSessionManager = agentSessionManager;
        _workspace = workspace;
    }

    public Task<List<SessionInfo>> GetSessionsAsync()
    {
        try
        {
            var agentSessions = _agentSessionManager.ListSessions()
                .Where(s => s.Key.StartsWith("webui:"))
                .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt ?? DateTimeOffset.MinValue)
                .Select(s => new SessionInfo
                {
                    Id = s.Key.Replace("webui:", ""),
                    Title = GetSessionTitle(s.Key),
                    CreatedAt = (s.CreatedAt ?? DateTimeOffset.Now).DateTime,
                    UpdatedAt = (s.UpdatedAt ?? DateTimeOffset.Now).DateTime
                })
                .ToList();
            
            return Task.FromResult(agentSessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing sessions");
            return Task.FromResult(new List<SessionInfo>());
        }
    }
    
    private string GetSessionTitle(string sessionKey)
    {
        try
        {
            // 尝试从会话文件的第一条用户消息提取标题
            var sessionsPath = _workspace.GetSessionsPath();
            var sessionFile = Path.Combine(sessionsPath, $"{sessionKey}.jsonl");
            
            if (File.Exists(sessionFile))
            {
                var lines = File.ReadAllLines(sessionFile);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("{\"metadata\":"))
                        continue;
                    
                    try
                    {
                        var msg = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(line);
                        if (msg.TryGetProperty("role", out var roleElement) && 
                            msg.TryGetProperty("content", out var contentElement))
                        {
                            var role = roleElement.GetString()?.ToLower();
                            if (role == "user")
                            {
                                var content = contentElement.GetString() ?? string.Empty;
                                // 提取前30个字符作为标题
                                if (content.Length > 30)
                                    return content.Substring(0, 30) + "...";
                                return content;
                            }
                        }
                    }
                    catch
                    {
                        // 忽略解析错误
                    }
                }
            }
        }
        catch
        {
            // 如果读取失败，使用默认标题
        }
        
        // 默认标题
        var sessionId = sessionKey.Replace("webui:", "");
        return $"会话 {sessionId.Substring(0, Math.Min(8, sessionId.Length))}";
    }

    public async Task<SessionInfo?> GetSessionAsync(string sessionId)
    {
        try
        {
            var sessionKey = $"webui:{sessionId}";
            var agentSession = await _agentSessionManager.GetOrCreateSessionAsync(sessionKey);
            
            if (agentSession == null)
                return null;
            
            return new SessionInfo
            {
                Id = sessionId,
                Title = GetSessionTitle(sessionKey),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<SessionInfo> CreateSessionAsync(string? title = null)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var sessionKey = $"webui:{sessionId}";
        
        try
        {
            // 创建 Agent 会话
            await _agentSessionManager.GetOrCreateSessionAsync(sessionKey);
            
            var session = new SessionInfo
            {
                Id = sessionId,
                Title = title ?? $"会话 {DateTime.Now:MM-dd HH:mm}",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
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

    public async Task<List<MessageInfo>> GetMessagesAsync(string sessionId)
    {
        try
        {
            var sessionKey = $"webui:{sessionId}";
            
            // 确保会话存在
            await _agentSessionManager.GetOrCreateSessionAsync(sessionKey);
            
            // 从会话文件读取消息（如果存在）
            var sessionsPath = _workspace.GetSessionsPath();
            var sessionFile = Path.Combine(sessionsPath, $"{sessionKey}.jsonl");
            
            if (!File.Exists(sessionFile))
                return new List<MessageInfo>();
            
            var messages = new List<MessageInfo>();
            var lines = await File.ReadAllLinesAsync(sessionFile);
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("{\"metadata\":"))
                    continue;
                
                try
                {
                    var msg = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(line);
                    if (msg.TryGetProperty("role", out var roleElement) && 
                        msg.TryGetProperty("content", out var contentElement))
                    {
                        var role = roleElement.GetString()?.ToLower() ?? "user";
                        var content = contentElement.GetString() ?? string.Empty;
                        
                        messages.Add(new MessageInfo
                        {
                            Id = $"{sessionId}_{messages.Count}",
                            SessionId = sessionId,
                            Role = role,
                            Content = content,
                            Timestamp = DateTime.Now
                        });
                    }
                }
                catch
                {
                    // 忽略无法解析的行
                }
            }
            
            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for session {SessionId}", sessionId);
            return new List<MessageInfo>();
        }
    }

    public Task<MessageInfo> AddMessageAsync(string sessionId, string role, string content)
    {
        // 消息现在通过 AgentService 处理并自动保存到会话中
        // 这个方法只返回一个占位符，实际消息会在 Agent 处理后出现在历史中
        var message = new MessageInfo
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            Role = role,
            Content = content,
            Timestamp = DateTime.Now
        };

        _logger.LogInformation("Message placeholder created for session {SessionId}: {Role}", sessionId, role);
        return Task.FromResult(message);
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        try
        {
            var sessionKey = $"webui:{sessionId}";
            await _agentSessionManager.ClearSessionAsync(sessionKey);
            _logger.LogInformation("Deleted session: {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting session {SessionId}", sessionId);
            throw;
        }
    }
}
