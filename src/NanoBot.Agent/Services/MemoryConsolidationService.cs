using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;
using NanoBot.Core.Memory;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Memory;
using NanoBot.Agent.Extensions;

namespace NanoBot.Agent.Services;

/// <summary>
/// Handles memory consolidation for sessions.
/// </summary>
public sealed class MemoryConsolidationService
{
    private readonly IMemoryStore? _memoryStore;
    private readonly IWorkspaceManager _workspace;
    private readonly ISessionManager _sessionManager;
    private readonly int _memoryWindow;
    private readonly ILogger<MemoryConsolidationService>? _logger;
    private readonly Func<string, IChatClient?> _getChatClient;

    public MemoryConsolidationService(
        IMemoryStore? memoryStore,
        IWorkspaceManager workspace,
        ISessionManager sessionManager,
        int memoryWindow,
        Func<string, IChatClient?> getChatClient,
        ILogger<MemoryConsolidationService>? logger = null)
    {
        _memoryStore = memoryStore;
        _workspace = workspace;
        _sessionManager = sessionManager;
        _memoryWindow = memoryWindow;
        _getChatClient = getChatClient;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to consolidate memory for a session if needed.
    /// </summary>
    public async Task TryConsolidateAsync(AgentSession session, string sessionKey, CancellationToken cancellationToken = default)
    {
        if (_memoryStore == null)
            return;

        try
        {
            var messages = GetSessionMessages(session);
            if (messages.Count <= _memoryWindow)
            {
                _logger?.LogDebug("Memory consolidation skipped: {Count} messages <= window {Window}", messages.Count, _memoryWindow);
                return;
            }

            var chatClient = _getChatClient(sessionKey);
            if (chatClient == null)
            {
                _logger?.LogWarning("Could not get ChatClient for memory consolidation");
                return;
            }

            var consolidator = new MemoryConsolidator(
                chatClient,
                _memoryStore,
                _workspace,
                _memoryWindow);

            _logger?.LogInformation("Starting memory consolidation for {Count} messages", messages.Count);
            var lastConsolidated = _sessionManager.GetLastConsolidated(sessionKey);
            var newLastConsolidated = await consolidator.ConsolidateAsync(messages, lastConsolidated, archiveAll: false, cancellationToken);

            if (newLastConsolidated.HasValue)
            {
                _sessionManager.SetLastConsolidated(sessionKey, newLastConsolidated.Value);
                await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);
            }
            _logger?.LogInformation("Memory consolidation completed");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Memory consolidation failed");
        }
    }

    /// <summary>
    /// Consolidates all messages in a session (used for /new command).
    /// </summary>
    public async Task ConsolidateAllAsync(AgentSession session, string sessionKey, CancellationToken cancellationToken = default)
    {
        if (_memoryStore == null)
            return;

        try
        {
            var chatClient = _getChatClient(sessionKey);
            if (chatClient == null)
            {
                _logger?.LogWarning("Could not get ChatClient for memory consolidation");
                return;
            }

            var consolidator = new MemoryConsolidator(
                chatClient,
                _memoryStore,
                _workspace,
                _memoryWindow,
                null);

            var messages = GetSessionMessages(session);
            await consolidator.ConsolidateAsync(messages, 0, archiveAll: true, cancellationToken);
            _logger?.LogInformation("Full memory consolidation completed for session");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to consolidate memory for session");
        }
    }

    /// <summary>
    /// Gets all messages from a session.
    /// </summary>
    public static List<ChatMessage> GetSessionMessages(AgentSession session)
    {
        return session.GetAllMessages().ToList();
    }
}
