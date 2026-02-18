using NanoBot.Core.Bus;

namespace NanoBot.Core.Channels;

public interface IChannel
{
    string Id { get; }
    string Type { get; }
    bool IsConnected { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default);

    event EventHandler<InboundMessage>? MessageReceived;
}
