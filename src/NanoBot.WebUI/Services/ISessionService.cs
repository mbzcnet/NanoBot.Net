namespace NanoBot.WebUI.Services;

public interface ISessionService
{
    Task<List<SessionInfo>> GetSessionsAsync();
    Task<SessionInfo?> GetSessionAsync(string sessionId);
    Task<SessionInfo> CreateSessionAsync(string? title = null);
    Task<List<MessageInfo>> GetMessagesAsync(string sessionId);
    Task<MessageInfo> AddMessageAsync(string sessionId, string role, string content);
    Task DeleteSessionAsync(string sessionId);
}

public class SessionInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MessageInfo
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
