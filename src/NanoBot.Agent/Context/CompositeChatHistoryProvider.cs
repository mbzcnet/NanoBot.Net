using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace NanoBot.Agent.Context;

public class CompositeChatHistoryProvider : ChatHistoryProvider
{
    private readonly List<ChatHistoryProvider> _providers;

    public CompositeChatHistoryProvider(List<ChatHistoryProvider> providers)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    protected override async ValueTask<IEnumerable<ChatMessage>> InvokingCoreAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var allMessages = new List<ChatMessage>();

        foreach (var provider in _providers)
        {
            var messages = await provider.InvokingAsync(context, cancellationToken);
            allMessages.AddRange(messages);
        }

        return allMessages;
    }

    protected override async ValueTask InvokedCoreAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            await provider.InvokedAsync(context, cancellationToken);
        }
    }
}
