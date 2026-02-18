using Microsoft.Extensions.Logging;
using NanoBot.Core.Bus;
using NanoBot.Core.Channels;

namespace NanoBot.Channels.Abstractions;

public abstract class ChannelBase : IChannel
{
    protected readonly ILogger _logger;
    protected readonly IMessageBus Bus;
    protected bool _running;

    public abstract string Id { get; }
    public abstract string Type { get; }
    public bool IsConnected => _running;

    public event EventHandler<InboundMessage>? MessageReceived;

    protected ChannelBase(IMessageBus bus, ILogger logger)
    {
        Bus = bus;
        _logger = logger;
    }

    public abstract Task StartAsync(CancellationToken cancellationToken = default);
    public abstract Task StopAsync(CancellationToken cancellationToken = default);
    public abstract Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default);

    protected bool IsAllowed(string senderId, IReadOnlyList<string> allowList)
    {
        if (allowList == null || allowList.Count == 0)
            return true;

        if (allowList.Contains(senderId))
            return true;

        if (senderId.Contains('|'))
        {
            foreach (var part in senderId.Split('|'))
            {
                if (!string.IsNullOrEmpty(part) && allowList.Contains(part))
                    return true;
            }
        }

        return false;
    }

    protected async Task HandleMessageAsync(
        string senderId,
        string chatId,
        string content,
        IReadOnlyList<string>? media = null,
        IDictionary<string, object>? metadata = null)
    {
        var message = new InboundMessage
        {
            Channel = Type,
            SenderId = senderId,
            ChatId = chatId,
            Content = content,
            Media = media ?? Array.Empty<string>(),
            Metadata = metadata,
            Timestamp = DateTimeOffset.UtcNow
        };

        MessageReceived?.Invoke(this, message);
        await Bus.PublishInboundAsync(message);
    }

    protected virtual void OnMessageReceived(InboundMessage message)
    {
        MessageReceived?.Invoke(this, message);
    }
}
