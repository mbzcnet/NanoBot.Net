using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent.Context;

public class FileBackedChatHistoryProvider : ChatHistoryProvider
{
    private readonly IWorkspaceManager _workspace;
    private readonly int _maxHistoryEntries;
    private readonly ILogger<FileBackedChatHistoryProvider>? _logger;

    public FileBackedChatHistoryProvider(
        IWorkspaceManager workspace,
        int maxHistoryEntries = 100,
        ILogger<FileBackedChatHistoryProvider>? logger = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _maxHistoryEntries = maxHistoryEntries;
        _logger = logger;
    }

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        if (context.Session == null)
        {
            return [];
        }

        // Try to get messages from session state
        if (context.Session.StateBag.TryGetValue<List<ChatMessage>>(StateKey, out var messages) && messages != null)
        {
            _logger?.LogDebug("Loaded {Count} messages from session state", messages.Count);
            return messages;
        }

        _logger?.LogDebug("No messages found in session state");
        return [];
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

        // Also append to history file for logging
        await AppendToHistoryFileAsync(context, cancellationToken);
    }

    private async ValueTask AppendToHistoryFileAsync(InvokedContext context, CancellationToken cancellationToken)
    {
        var historyPath = _workspace.GetHistoryFile();
        var directory = Path.GetDirectoryName(historyPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sb = new StringBuilder();

        foreach (var message in context.RequestMessages)
        {
            var text = message.Text ?? string.Empty;
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm}] {message.Role.ToString().ToLowerInvariant()}: {text}");
        }

        if (context.ResponseMessages != null)
        {
            foreach (var message in context.ResponseMessages)
            {
                var text = message.Text ?? string.Empty;
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm}] {message.Role.ToString().ToLowerInvariant()}: {text}");
            }
        }

        if (sb.Length > 0)
        {
            await File.AppendAllTextAsync(historyPath, sb.ToString(), cancellationToken);
        }
    }

    private static IEnumerable<ChatMessage> ParseHistoryToMessages(IEnumerable<string> lines)
    {
        var messages = new List<ChatMessage>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parsed = ParseHistoryLine(line);
            if (parsed != null)
            {
                messages.Add(parsed);
            }
        }

        return messages;
    }

    private static ChatMessage? ParseHistoryLine(string line)
    {
        var bracketIndex = line.IndexOf(']');
        if (bracketIndex < 0)
        {
            return null;
        }

        var afterBracket = line.AsSpan(bracketIndex + 1).Trim();
        var colonIndex = afterBracket.IndexOf(':');
        if (colonIndex < 0)
        {
            return null;
        }

        var roleSpan = afterBracket[..colonIndex].Trim();
        var contentSpan = afterBracket[(colonIndex + 1)..].Trim();

        var role = roleSpan.ToString() switch
        {
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => ChatRole.User
        };

        return new ChatMessage(role, contentSpan.ToString());
    }
}
