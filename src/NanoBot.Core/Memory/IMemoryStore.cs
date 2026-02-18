using Microsoft.Extensions.AI;

namespace NanoBot.Core.Memory;

public interface IMemoryStore
{
    Task<string> LoadAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(
        IEnumerable<ChatMessage> requestMessages,
        IEnumerable<ChatMessage> responseMessages,
        CancellationToken cancellationToken = default);

    Task AppendHistoryAsync(string entry, CancellationToken cancellationToken = default);

    string GetMemoryContext();
}
