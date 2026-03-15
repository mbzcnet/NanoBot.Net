using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent.Context;

public class FileBackedChatHistoryProvider : ChatHistoryProvider
{
    private readonly int _maxHistoryEntries;
    private readonly ILogger<FileBackedChatHistoryProvider>? _logger;

    public FileBackedChatHistoryProvider(
        IWorkspaceManager workspace,
        int maxHistoryEntries = 100,
        ILogger<FileBackedChatHistoryProvider>? logger = null)
    {
        _maxHistoryEntries = maxHistoryEntries;
        _logger = logger;
    }

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        if (context.Session == null)
        {
            return ValueTask.FromResult<IEnumerable<ChatMessage>>([]);
        }

        if (context.Session.StateBag.TryGetValue<List<ChatMessage>>(StateKey, out var messages) && messages != null)
        {
            _logger?.LogDebug("Loaded {Count} messages from session state", messages.Count);
            return ValueTask.FromResult<IEnumerable<ChatMessage>>(messages);
        }

        _logger?.LogDebug("No messages found in session state");
        return ValueTask.FromResult<IEnumerable<ChatMessage>>([]);
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
        if (context.Session == null)
        {
            return;
        }

        // Get existing messages from session state and create a new list
        var allMessages = new List<ChatMessage>();
        
        if (context.Session.StateBag.TryGetValue<List<ChatMessage>>(StateKey, out var existing) && existing != null)
        {
            allMessages.AddRange(existing);
        }

        // Add request messages (these are the new user messages, already filtered by base class)
        allMessages.AddRange(context.RequestMessages);

        // Add response messages
        if (context.ResponseMessages != null)
        {
            allMessages.AddRange(context.ResponseMessages);
        }

        // Keep only the last N messages
        if (allMessages.Count > _maxHistoryEntries)
        {
            var toRemove = allMessages.Count - _maxHistoryEntries;
            allMessages.RemoveRange(0, toRemove);
            _logger?.LogDebug("Trimmed {Count} old messages, keeping {Remaining}", toRemove, allMessages.Count);
        }

        // Store back to session state
        context.Session.StateBag.SetValue(StateKey, allMessages);
        _logger?.LogDebug("Stored {Count} messages to session state", allMessages.Count);
    }
}
