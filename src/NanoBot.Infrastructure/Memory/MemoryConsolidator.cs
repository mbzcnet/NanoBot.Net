using System.Text;
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

    public async Task<int?> ConsolidateAsync(
        IList<ChatMessage> messages,
        int lastConsolidatedIndex,
        bool archiveAll = false,
        CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count == 0)
        {
            _logger?.LogDebug("No messages to consolidate");
            return null;
        }

        var keepCount = archiveAll ? 0 : _memoryWindow / 2;
        
        if (!archiveAll)
        {
            if (messages.Count <= keepCount)
            {
                _logger?.LogDebug("No consolidation needed (messages={Count}, keep={KeepCount})", messages.Count, keepCount);
                return null;
            }

            var messagesToProcess = messages.Count - lastConsolidatedIndex;
            if (messagesToProcess <= 0)
            {
                _logger?.LogDebug("No new messages to consolidate (lastConsolidated={Index}, total={Total})", lastConsolidatedIndex, messages.Count);
                return null;
            }
        }

        var startIndex = archiveAll ? 0 : lastConsolidatedIndex;
        var endIndex = archiveAll ? messages.Count : messages.Count - keepCount;
        
        if (startIndex >= endIndex)
        {
            return null;
        }

        var oldMessages = messages.Skip(startIndex).Take(endIndex - startIndex).ToList();
        if (oldMessages.Count == 0)
        {
            return null;
        }

        _logger?.LogInformation("Memory consolidation started: {Total} total, {ToProcess} to consolidate, {Keep} keep",
            messages.Count, oldMessages.Count, keepCount);

        var conversation = BuildConversationText(oldMessages);
        var currentMemory = await _memoryStore.LoadAsync(cancellationToken);

        var prompt = BuildConsolidationPrompt(currentMemory, conversation);

        var saveMemoryTool = AIFunctionFactory.Create(
            (string history_entry, string memory_update) => string.Empty,
            new AIFunctionFactoryOptions
            {
                Name = "save_memory",
                Description = "Save the memory consolidation result to persistent storage."
            });

        try
        {
            var response = await _chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, "You are a memory consolidation agent. Call the save_memory tool with your consolidation of the conversation."),
                    new ChatMessage(ChatRole.User, prompt)
                ],
                options: new ChatOptions { Tools = [saveMemoryTool] },
                cancellationToken: cancellationToken);

            var call = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .FirstOrDefault(fc => string.Equals(fc.Name, "save_memory", StringComparison.OrdinalIgnoreCase));

            if (call?.Arguments is not IDictionary<string, object?> args)
            {
                _logger?.LogWarning("Memory consolidation: LLM did not call save_memory, skipping");
                return null;
            }

            var historyEntry = args.TryGetValue("history_entry", out var he) ? he?.ToString() : null;
            var memoryUpdate = args.TryGetValue("memory_update", out var mu) ? mu?.ToString() : null;

            if (!string.IsNullOrWhiteSpace(historyEntry))
            {
                await _memoryStore.AppendHistoryAsync(historyEntry!, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(memoryUpdate) && memoryUpdate != currentMemory)
            {
                await WriteMemoryAsync(memoryUpdate!, cancellationToken);
            }

            _logger?.LogInformation("Memory consolidation done: {Total} messages", messages.Count);

            // 与原项目一致：archive_all 时 last_consolidated=0；否则 = total - keepCount
            var newLastConsolidated = archiveAll ? 0 : messages.Count - keepCount;
            return newLastConsolidated;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Memory consolidation failed");
            return null;
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

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            var role = message.Role == ChatRole.User ? "USER" : "ASSISTANT";
            sb.AppendLine($"[{timestamp}] {role}: {message.Text}");
        }
        return sb.ToString();
    }

    private static string BuildConsolidationPrompt(string currentMemory, string conversation)
    {
        return $""""
Process this conversation and call the save_memory tool with your consolidation.

Arguments:
- history_entry: A paragraph (2-5 sentences) summarizing key events/decisions/topics. Start with [YYYY-MM-DD HH:MM]. Include detail useful for grep search.
- memory_update: Full updated long-term memory as markdown. Include all existing facts plus new ones. Return unchanged if nothing new.

## Current Long-term Memory
{currentMemory ?? "(empty)"}

## Conversation to Process
{conversation}
"""";
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
