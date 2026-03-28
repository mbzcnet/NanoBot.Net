namespace NanoBot.Core.Sessions;

/// <summary>
/// Session service interface - combines ISessionManager and IMessageStore.
/// </summary>
/// <remarks>
/// This interface is the composition of session management and message storage capabilities.
/// Use <see cref="ISessionManager"/> for session metadata operations or <see cref="IMessageStore"/> for message operations.
/// </remarks>
public interface ISessionService : ISessionManager, IMessageStore
{
}
