namespace NanoBot.Core.Sessions;

/// <summary>
/// Manages session metadata (creation, deletion, listing).
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Gets all sessions.
    /// </summary>
    Task<List<SessionInfo>> GetSessionsAsync();

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    Task<SessionInfo?> GetSessionAsync(string sessionId);

    /// <summary>
    /// Creates a new session.
    /// </summary>
    Task<SessionInfo> CreateSessionAsync(string? title = null, string? profileId = null);

    /// <summary>
    /// Sets the profile for a session.
    /// </summary>
    Task SetSessionProfileAsync(string sessionId, string profileId);

    /// <summary>
    /// Renames a session.
    /// </summary>
    Task RenameSessionAsync(string sessionId, string newTitle);

    /// <summary>
    /// Deletes a session.
    /// </summary>
    Task DeleteSessionAsync(string sessionId);
}
