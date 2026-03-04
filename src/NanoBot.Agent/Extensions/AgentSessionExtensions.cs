using System.Reflection;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace NanoBot.Agent.Extensions;

public static class AgentSessionExtensions
{
    private static readonly FieldInfo? ChatClientField = typeof(Microsoft.Agents.AI.ChatClientAgent)
        .GetField("_chatClient", BindingFlags.NonPublic | BindingFlags.Instance);

    public static IReadOnlyList<ChatMessage> GetAllMessages(this AgentSession session)
    {
        if (session == null)
            return Array.Empty<ChatMessage>();

        // 从 Session.StateBag 中获取消息，这是 FileBackedChatHistoryProvider 存储消息的地方
        // StateKey 默认是 provider 类的名称
        var provider = session.GetService<ChatHistoryProvider>();
        if (provider != null)
        {
            var stateKey = provider.GetType().Name;
            if (session.StateBag.TryGetValue<List<ChatMessage>>(stateKey, out var messages) && messages != null)
            {
                return messages;
            }
        }

        // 尝试常见的 state key
        var commonKeys = new[] { "FileBackedChatHistoryProvider", "InMemoryChatHistoryProvider", "ChatHistoryProvider" };
        foreach (var key in commonKeys)
        {
            if (session.StateBag.TryGetValue<List<ChatMessage>>(key, out var messages) && messages != null && messages.Count > 0)
            {
                return messages;
            }
        }

        return Array.Empty<ChatMessage>();
    }

    public static IChatClient? GetChatClient(this ChatClientAgent agent)
    {
        if (agent == null)
            return null;

        return ChatClientField?.GetValue(agent) as IChatClient;
    }
}
