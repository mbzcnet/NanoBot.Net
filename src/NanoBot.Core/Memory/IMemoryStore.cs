using Microsoft.Extensions.AI;

namespace NanoBot.Core.Memory;

public interface IMemoryStore
{
    Task<string> LoadAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(
        IEnumerable<ChatMessage> requestMessages,
        IEnumerable<ChatMessage> responseMessages,
        CancellationToken cancellationToken = default);

    string GetMemoryContext();

    /// <summary>
    /// Appends a history entry to the grep-searchable history log.
    /// </summary>
    Task AppendHistoryAsync(string entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the grep-searchable history context.
    /// </summary>
    string GetHistoryContext();

    /// <summary>
    /// Gets the path to the history file.
    /// </summary>
    string GetHistoryFilePath();
}
