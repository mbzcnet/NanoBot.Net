using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using NanoBot.Agent.Extensions;

namespace NanoBot.Agent;

/// <summary>
/// Extension methods for ChatClientAgent to access internal state.
/// Encapsulates reflection-based access to private members.
/// </summary>
public static class AgentExtensions
{
    private static readonly FieldInfo? ChatClientField = typeof(ChatClientAgent)
        .GetField("_chatClient", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo? AiContextProvidersField = typeof(ChatClientAgent)
        .GetField("_aiContextProviders", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo? ChatHistoryProviderField = typeof(ChatClientAgent)
        .GetField("_chatHistoryProvider", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo? OptionsField = typeof(ChatClientAgent)
        .GetField("_agentOptions", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// Gets the ChatClient from a ChatClientAgent, using reflection as fallback.
    /// Uses the existing extension method from AgentSessionExtensions.
    /// </summary>
    public static IChatClient? GetChatClientSafe(this ChatClientAgent agent)
    {
        // Use the existing extension method
        return agent.GetChatClient();
    }

    /// <summary>
    /// Gets the AIContextProviders from a ChatClientAgent.
    /// </summary>
    public static IEnumerable<AIContextProvider>? GetAiContextProviders(this ChatClientAgent agent)
    {
        if (AiContextProvidersField != null)
        {
            return AiContextProvidersField.GetValue(agent) as IEnumerable<AIContextProvider>;
        }
        return null;
    }

    /// <summary>
    /// Gets the ChatHistoryProvider from a ChatClientAgent.
    /// </summary>
    public static ChatHistoryProvider? GetChatHistoryProvider(this ChatClientAgent agent)
    {
        if (ChatHistoryProviderField != null)
        {
            return ChatHistoryProviderField.GetValue(agent) as ChatHistoryProvider;
        }
        return null;
    }

    /// <summary>
    /// Gets the ChatClientAgentOptions from a ChatClientAgent.
    /// </summary>
    public static ChatClientAgentOptions? GetOptions(this ChatClientAgent agent)
    {
        if (OptionsField != null)
        {
            return OptionsField.GetValue(agent) as ChatClientAgentOptions;
        }
        return null;
    }
}
