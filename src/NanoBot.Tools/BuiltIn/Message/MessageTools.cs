using Microsoft.Extensions.AI;
using NanoBot.Core.Bus;

namespace NanoBot.Tools.BuiltIn;

public static class MessageTools
{
    public static AITool CreateMessageTool(IMessageBus? messageBus, string? defaultChannel = null, string? defaultChatId = null)
    {
        return AIFunctionFactory.Create(
            (string content, string? channel, string? chatId, CancellationToken cancellationToken) =>
                SendMessageAsync(content, channel ?? defaultChannel, chatId ?? defaultChatId, messageBus, cancellationToken),
            new AIFunctionFactoryOptions
            {
                Name = "message",
                Description = "Send a message to the user. Use this when you want to communicate something."
            });
    }

    private static async Task<string> SendMessageAsync(
        string content,
        string? channel,
        string? chatId,
        IMessageBus? messageBus,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(chatId))
            {
                return "Error: No target channel/chat specified";
            }

            if (messageBus == null)
            {
                return "Error: Message bus not available";
            }

            var message = new OutboundMessage
            {
                Channel = channel,
                ChatId = chatId,
                Content = content
            };

            await messageBus.PublishOutboundAsync(message, cancellationToken);

            return $"Message sent to {channel}:{chatId}";
        }
        catch (Exception ex)
        {
            return $"Error sending message: {ex.Message}";
        }
    }
}
