namespace NanoBot.Core.Sessions;

/// <summary>
/// Manages session messages (storage, retrieval, deletion).
/// </summary>
public interface IMessageStore
{
    /// <summary>
    /// Gets all messages for a session.
    /// </summary>
    Task<List<MessageInfo>> GetMessagesAsync(string sessionId);

    /// <summary>
    /// Adds a message to a session.
    /// </summary>
    Task<MessageInfo> AddMessageAsync(string sessionId, string role, string content, List<AttachmentInfo>? attachments = null);

    /// <summary>
    /// Deletes messages from a specific index onwards.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <param name="fromIndex">Starting index (0-based) to delete from</param>
    Task DeleteMessagesFromAsync(string sessionId, int fromIndex);
}
