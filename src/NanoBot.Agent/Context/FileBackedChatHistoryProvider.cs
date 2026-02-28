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
        var historyPath = _workspace.GetHistoryFile();

        if (!File.Exists(historyPath))
        {
            return [];
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(historyPath, cancellationToken);
            return ParseHistoryToMessages(lines.TakeLast(_maxHistoryEntries * 2));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read history file: {HistoryPath}", historyPath);
            return [];
        }
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
        var historyPath = _workspace.GetHistoryFile();
        var directory = Path.GetDirectoryName(historyPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sb = new StringBuilder();

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
