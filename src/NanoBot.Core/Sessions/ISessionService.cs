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
}
