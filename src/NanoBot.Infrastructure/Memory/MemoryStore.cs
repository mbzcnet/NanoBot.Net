using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Core.Memory;
using NanoBot.Core.Workspace;

namespace NanoBot.Infrastructure.Memory;

public class MemoryStore : IMemoryStore
{
    private readonly IWorkspaceManager _workspace;
    private readonly MemoryConfig _config;
    private readonly ILogger<MemoryStore>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedMemory;

    public MemoryStore(
        IWorkspaceManager workspace,
        MemoryConfig config,
        ILogger<MemoryStore>? logger = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    public async Task<string> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedMemory != null)
            {
                return _cachedMemory;
            }

            var memoryPath = _workspace.GetMemoryFile();
            if (!File.Exists(memoryPath))
            {
                _logger?.LogDebug("Memory file does not exist: {Path}", memoryPath);
                return string.Empty;
            }

            var content = await File.ReadAllTextAsync(memoryPath, cancellationToken);
            _cachedMemory = content;
            return content;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(
        IEnumerable<ChatMessage> requestMessages,
        IEnumerable<ChatMessage> responseMessages,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            _logger?.LogDebug("Memory is disabled, skipping update");
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var memoryPath = _workspace.GetMemoryFile();
            var directory = Path.GetDirectoryName(memoryPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var currentMemory = _cachedMemory ?? await LoadWithoutLockAsync(cancellationToken);
            var newContent = BuildUpdatedMemory(currentMemory, requestMessages, responseMessages);

            if (newContent != currentMemory)
            {
                await File.WriteAllTextAsync(memoryPath, newContent, cancellationToken);
                _cachedMemory = newContent;
                _logger?.LogInformation("Memory updated: {Path}", memoryPath);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AppendHistoryAsync(string entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var historyPath = _workspace.GetHistoryFile();
            var directory = Path.GetDirectoryName(historyPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var text = entry.TrimEnd();
            await File.AppendAllTextAsync(historyPath, text + "\n\n", cancellationToken);
            _logger?.LogDebug("History appended: {Path}", historyPath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public string GetMemoryContext()
    {
        var memory = _cachedMemory ?? LoadAsync().GetAwaiter().GetResult();
        return string.IsNullOrEmpty(memory) ? string.Empty : $"## Long-term Memory\n{memory}";
    }

    private async Task<string> LoadWithoutLockAsync(CancellationToken cancellationToken)
    {
        var memoryPath = _workspace.GetMemoryFile();
        if (!File.Exists(memoryPath))
        {
            return string.Empty;
        }

        return await File.ReadAllTextAsync(memoryPath, cancellationToken);
    }

    protected virtual string BuildUpdatedMemory(
        string currentMemory,
        IEnumerable<ChatMessage> requestMessages,
        IEnumerable<ChatMessage> responseMessages)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(currentMemory))
        {
            sb.AppendLine(currentMemory);
            sb.AppendLine();
        }

        sb.AppendLine("## Recent Conversations");
        sb.AppendLine();

        var requestList = requestMessages.ToList();
        var responseList = responseMessages.ToList();

        foreach (var message in requestList)
        {
            if (!string.IsNullOrWhiteSpace(message.Text))
            {
                var text = FilterBase64Content(message.Text);
                sb.AppendLine($"- **User**: {TruncateText(text, 500)}");
            }
        }

        foreach (var message in responseList)
        {
            if (!string.IsNullOrWhiteSpace(message.Text))
            {
                var text = FilterBase64Content(message.Text);
                sb.AppendLine($"- **Assistant**: {TruncateText(text, 500)}");
            }
        }

        return sb.ToString();
    }

    private static string FilterBase64Content(string text)
    {
        // Filter out base64-encoded content (e.g., data:image/png;base64,...)
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"data:image/[a-zA-Z]+;base64,[A-Za-z0-9+/=]+",
            "[image]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }
}
