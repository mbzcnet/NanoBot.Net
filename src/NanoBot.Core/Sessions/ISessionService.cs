namespace NanoBot.Core.Sessions;

public interface ISessionService
{
    Task<List<SessionInfo>> GetSessionsAsync();
    Task<SessionInfo?> GetSessionAsync(string sessionId);
    Task<SessionInfo> CreateSessionAsync(string? title = null, string? profileId = null);
    Task SetSessionProfileAsync(string sessionId, string profileId);
    Task RenameSessionAsync(string sessionId, string newTitle);
    Task DeleteSessionAsync(string sessionId);
    Task<List<MessageInfo>> GetMessagesAsync(string sessionId);
    Task<MessageInfo> AddMessageAsync(string sessionId, string role, string content, List<AttachmentInfo>? attachments = null);
    
    /// <summary>
    /// 从指定索引位置删除消息及其之后的所有消息
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="fromIndex">开始删除的消息索引（0-based）</param>
    Task DeleteMessagesFromAsync(string sessionId, int fromIndex);
}
