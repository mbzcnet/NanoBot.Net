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

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var allMessages = new List<ChatMessage>();

        foreach (var provider in _providers)
        {
            // Call the protected method through reflection to get only history messages
            var provideMethod = provider.GetType().GetMethod("ProvideChatHistoryAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (provideMethod != null)
            {
                var task = provideMethod.Invoke(provider, new object[] { context, cancellationToken });
                if (task is ValueTask<IEnumerable<ChatMessage>> valueTask)
                {
                    var messages = await valueTask;
                    allMessages.AddRange(messages);
                }
            }
        }

        return allMessages;
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            // Call the protected method through reflection
            var storeMethod = provider.GetType().GetMethod("StoreChatHistoryAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (storeMethod != null)
            {
                var task = storeMethod.Invoke(provider, new object[] { context, cancellationToken });
                if (task is ValueTask valueTask)
                {
                    await valueTask;
                }
            }
        }
    }
}
