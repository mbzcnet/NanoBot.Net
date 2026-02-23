using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Memory;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent.Context;

public class MemoryConsolidationChatHistoryProvider : ChatHistoryProvider
{
    private static readonly ConcurrentDictionary<string, int> SessionConsolidationIndex = new();

    private readonly IChatClient _chatClient;
    private readonly IMemoryStore _memoryStore;
    private readonly IWorkspaceManager _workspace;
    private readonly int _memoryWindow;
    private readonly ILogger<MemoryConsolidationChatHistoryProvider>? _logger;

    public MemoryConsolidationChatHistoryProvider(
        IChatClient chatClient,
        IMemoryStore memoryStore,
        IWorkspaceManager workspace,
        int memoryWindow = 50,
        ILogger<MemoryConsolidationChatHistoryProvider>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _memoryWindow = memoryWindow;
        _logger = logger;
    }

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IEnumerable<ChatMessage>>([]);
    }

    protected override async ValueTask InvokedCoreAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
        if (context.InvokeException is not null)
        {
            return;
        }

        var session = context.Session;
        if (session == null)
        {
            return;
        }

        var conversationId = GetConversationId(session);
        if (string.IsNullOrEmpty(conversationId))
        {
            return;
        }

        var requestMessages = context.RequestMessages.ToList();
        var responseMessages = context.ResponseMessages?.ToList() ?? [];

        if (requestMessages.Count == 0 || responseMessages.Count == 0)
        {
            return;
        }

        var allMessages = new List<ChatMessage>();
        allMessages.AddRange(requestMessages);
        allMessages.AddRange(responseMessages);

        var lastConsolidated = SessionConsolidationIndex.GetValueOrDefault(conversationId, 0);
        var keepCount = _memoryWindow / 2;

        if (allMessages.Count <= keepCount)
        {
            _logger?.LogDebug("No consolidation needed (messages={Count}, keep={KeepCount})", allMessages.Count, keepCount);
            return;
        }

        var messagesToProcess = allMessages.Count - lastConsolidated;
        if (messagesToProcess <= 0)
        {
            _logger?.LogDebug("No new messages to consolidate (lastConsolidated={Index}, total={Total})", lastConsolidated, allMessages.Count);
            return;
        }

        var startIndex = lastConsolidated;
        var endIndex = allMessages.Count - keepCount;

        if (startIndex >= endIndex)
        {
            return;
        }

        var oldMessages = allMessages.Skip(startIndex).Take(endIndex - startIndex).ToList();
        if (oldMessages.Count == 0)
        {
            return;
        }

        _logger?.LogInformation("Memory consolidation started: {Total} total, {ToProcess} to consolidate, {Keep} keep",
            allMessages.Count, oldMessages.Count, keepCount);

        try
        {
            var conversation = BuildConversationText(oldMessages);
            var currentMemory = await _memoryStore.LoadAsync(cancellationToken);
            var prompt = BuildConsolidationPrompt(currentMemory, conversation);

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
                _logger?.LogWarning(ex, "Memory consolidation: failed to parse JSON response: {Response}", text[..Math.Min(200, text.Length)]);
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
                    var memoryPath = _workspace.GetMemoryFile();
                    var directory = Path.GetDirectoryName(memoryPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    await File.WriteAllTextAsync(memoryPath, updateText, cancellationToken);
                    _logger?.LogInformation("Memory file updated: {Path}", memoryPath);
                }
            }

            SessionConsolidationIndex[conversationId] = allMessages.Count - keepCount;
            _logger?.LogInformation("Memory consolidation done: {Total} messages, lastConsolidated={Index}", allMessages.Count, allMessages.Count - keepCount);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Memory consolidation failed");
        }
    }

    private static string GetConversationId(AgentSession session)
    {
        var property = session.GetType().GetProperty("ConversationId");
        if (property?.GetValue(session) is string conversationId)
        {
            return conversationId;
        }
        return session.GetHashCode().ToString();
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
}
