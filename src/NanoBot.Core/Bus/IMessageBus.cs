namespace NanoBot.Core.Bus;

public interface IMessageBus : IDisposable
{
    ValueTask PublishInboundAsync(InboundMessage message, CancellationToken ct = default);

    ValueTask<InboundMessage> ConsumeInboundAsync(CancellationToken ct = default);

    ValueTask PublishOutboundAsync(OutboundMessage message, CancellationToken ct = default);

    ValueTask<OutboundMessage> ConsumeOutboundAsync(CancellationToken ct = default);

    void SubscribeOutbound(string channel, Func<OutboundMessage, Task> callback);

    Task StartDispatcherAsync(CancellationToken ct = default);

    void Stop();

    int InboundSize { get; }

    int OutboundSize { get; }
}
