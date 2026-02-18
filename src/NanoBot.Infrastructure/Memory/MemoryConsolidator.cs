using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Memory;
using NanoBot.Core.Workspace;

namespace NanoBot.Infrastructure.Memory;

public class MemoryConsolidator
{
    private readonly IChatClient _chatClient;
    private readonly IMemoryStore _memoryStore;
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<MemoryConsolidator>? _logger;
    private readonly int _memoryWindow;

    public MemoryConsolidator(
        IChatClient chatClient,
        IMemoryStore memoryStore,
        IWorkspaceManager workspace,
        int memoryWindow = 50,
        ILogger<MemoryConsolidator>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _memoryWindow = memoryWindow;
        _logger = logger;
    }

    public async Task ConsolidateAsync(
        IList<ChatMessage> messages,
        int lastConsolidatedIndex,
        bool archiveAll = false,
        CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count == 0)
        {
            _logger?.LogDebug("No messages to consolidate");
            return;
        }

        var keepCount = archiveAll ? 0 : _memoryWindow / 2;
        
        if (!archiveAll)
        {
            if (messages.Count <= keepCount)
            {
                _logger?.LogDebug("No consolidation needed (messages={Count}, keep={KeepCount})", messages.Count, keepCount);
                return;
            }

            var messagesToProcess = messages.Count - lastConsolidatedIndex;
            if (messagesToProcess <= 0)
            {
                _logger?.LogDebug("No new messages to consolidate (lastConsolidated={Index}, total={Total})", lastConsolidatedIndex, messages.Count);
                return;
            }
        }

        var startIndex = archiveAll ? 0 : lastConsolidatedIndex;
        var endIndex = archiveAll ? messages.Count : messages.Count - keepCount;
        
        if (startIndex >= endIndex)
        {
            return;
        }

        var oldMessages = messages.Skip(startIndex).Take(endIndex - startIndex).ToList();
        if (oldMessages.Count == 0)
        {
            return;
        }

        _logger?.LogInformation("Memory consolidation started: {Total} total, {ToProcess} to consolidate, {Keep} keep",
            messages.Count, oldMessages.Count, keepCount);

        var conversation = BuildConversationText(oldMessages);
        var currentMemory = await _memoryStore.LoadAsync(cancellationToken);

        var prompt = BuildConsolidationPrompt(currentMemory, conversation);

        try
        {
            var response = await _chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, "You are a memory consolidation agent. Respond only with valid JSON."),
                    new ChatMessage(ChatRole.User, prompt)
                ],
                cancellationToken: cancellationToken);

            var text = response.Messages.FirstOrDefault()?.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                _logger?.LogWarning("Memory consolidation: LLM returned empty response, skipping");
                return;
            }

            text = CleanJsonResponse(text);
            
            JsonObject? result;
            try
            {
                result = JsonNode.Parse(text) as JsonObject;
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Memory consolidation: failed to parse JSON response: {Response}", text?[..Math.Min(200, text.Length)]);
                return;
            }

            if (result == null)
            {
                _logger?.LogWarning("Memory consolidation: unexpected response type, skipping");
                return;
            }

            if (result.TryGetPropertyValue("history_entry", out var historyEntry) && historyEntry != null)
            {
                var entryText = historyEntry.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(entryText))
                {
                    await _memoryStore.AppendHistoryAsync(entryText, cancellationToken);
                }
            }

            if (result.TryGetPropertyValue("memory_update", out var memoryUpdate) && memoryUpdate != null)
            {
                var updateText = memoryUpdate.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(updateText) && updateText != currentMemory)
                {
                    await WriteMemoryAsync(updateText, cancellationToken);
                }
            }

            _logger?.LogInformation("Memory consolidation done: {Total} messages", messages.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Memory consolidation failed");
        }
    }

    private static string BuildConversationText(IEnumerable<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                continue;
            }

            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
            var role = message.Role == ChatRole.User ? "USER" : "ASSISTANT";
            sb.AppendLine($"[{timestamp}] {role}: {message.Text}");
        }
        return sb.ToString();
    }

    private static string BuildConsolidationPrompt(string currentMemory, string conversation)
    {
        return $""""
You are a memory consolidation agent. Process this conversation and return a JSON object with exactly two keys:

1. "history_entry": A paragraph (2-5 sentences) summarizing the key events/decisions/topics. Start with a timestamp like [YYYY-MM-DD HH:MM]. Include enough detail to be useful when found by grep search later.

2. "memory_update": The updated long-term memory content. Add any new facts: user location, preferences, personal info, habits, project context, technical decisions, tools/services used. If nothing new, return the existing content unchanged.

## Current Long-term Memory
{currentMemory ?? "(empty)"}

## Conversation to Process
{conversation}

Respond with ONLY valid JSON, no markdown fences.
"""";
    }

    private static string CleanJsonResponse(string text)
    {
        if (text.StartsWith("```"))
        {
            var lines = text.Split('\n');
            if (lines.Length > 2)
            {
                text = string.Join('\n', lines.Skip(1).SkipLast(1));
            }
        }
        return text.Trim();
    }

    private async Task WriteMemoryAsync(string content, CancellationToken cancellationToken)
    {
        var memoryPath = _workspace.GetMemoryFile();
        var directory = Path.GetDirectoryName(memoryPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(memoryPath, content, cancellationToken);
        _logger?.LogInformation("Memory file updated: {Path}", memoryPath);
    }
}
