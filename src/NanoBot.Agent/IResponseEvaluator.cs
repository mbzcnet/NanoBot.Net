using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace NanoBot.Agent;

public interface IResponseEvaluator
{
    Task<bool> ShouldNotifyAsync(
        string response,
        string taskContext,
        IChatClient chatClient,
        CancellationToken ct = default);
}
