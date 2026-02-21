using NanoBot.Core.Bus;

namespace NanoBot.Agent;

public class BusProgressReporter : IProgressReporter
{
    private readonly IMessageBus _bus;
    private readonly string _channel;
    private readonly string _chatId;
    private readonly Dictionary<string, object>? _metadata;

    public BusProgressReporter(
        IMessageBus bus,
        string channel,
        string chatId,
        Dictionary<string, object>? metadata = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _channel = channel;
        _chatId = chatId;
        _metadata = metadata;
    }

    public async Task ReportProgressAsync(string content, CancellationToken cancellationToken = default)
    {
        await _bus.PublishOutboundAsync(new OutboundMessage
        {
            Channel = _channel,
            ChatId = _chatId,
            Content = content,
            Metadata = _metadata
        }, cancellationToken);
    }
}
