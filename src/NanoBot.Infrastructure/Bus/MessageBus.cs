using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Bus;

namespace NanoBot.Infrastructure.Bus;

public sealed class MessageBus : IMessageBus
{
    private readonly Channel<InboundMessage> _inboundChannel;
    private readonly Channel<OutboundMessage> _outboundChannel;
    private readonly Dictionary<string, Func<OutboundMessage, Task>> _outboundSubscribers;
    private readonly ILogger<MessageBus>? _logger;
    private readonly object _lock = new();
    private CancellationTokenSource? _dispatcherCts;
    private Task? _dispatcherTask;
    private bool _disposed;
    private bool _stopped;

    public int InboundSize => _inboundChannel.Reader.Count;

    public int OutboundSize => _outboundChannel.Reader.Count;

    public MessageBus(ILogger<MessageBus>? logger = null, int capacity = 1000)
    {
        _logger = logger;
        _outboundSubscribers = new Dictionary<string, Func<OutboundMessage, Task>>(StringComparer.OrdinalIgnoreCase);

        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };

        _inboundChannel = Channel.CreateBounded<InboundMessage>(options);
        _outboundChannel = Channel.CreateBounded<OutboundMessage>(options);
    }

    public async ValueTask PublishInboundAsync(InboundMessage message, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _inboundChannel.Writer.WriteAsync(message, ct);
        _logger?.LogDebug("Published inbound message from channel {Channel}", message.Channel);
    }

    public async ValueTask<InboundMessage> ConsumeInboundAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return await _inboundChannel.Reader.ReadAsync(ct);
    }

    public async ValueTask PublishOutboundAsync(OutboundMessage message, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _outboundChannel.Writer.WriteAsync(message, ct);
        _logger?.LogDebug("Published outbound message to channel {Channel}", message.Channel);
    }

    public async ValueTask<OutboundMessage> ConsumeOutboundAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return await _outboundChannel.Reader.ReadAsync(ct);
    }

    public void SubscribeOutbound(string channel, Func<OutboundMessage, Task> callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            _outboundSubscribers[channel] = callback;
            _logger?.LogInformation("Subscribed outbound handler for channel {Channel}", channel);
        }
    }

    public Task StartDispatcherAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_dispatcherTask != null && !_dispatcherTask.IsCompleted)
        {
            _logger?.LogWarning("Dispatcher is already running");
            return Task.CompletedTask;
        }

        _dispatcherCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _stopped = false;
        _dispatcherTask = Task.Run(() => DispatcherLoopAsync(_dispatcherCts.Token), ct);
        _logger?.LogInformation("Message bus dispatcher started");

        return Task.CompletedTask;
    }

    private async Task DispatcherLoopAsync(CancellationToken ct)
    {
        _logger?.LogDebug("Dispatcher loop started");

        try
        {
            await foreach (var message in _outboundChannel.Reader.ReadAllAsync(ct))
            {
                if (_stopped || ct.IsCancellationRequested)
                    break;

                await DispatchMessageAsync(message);
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Dispatcher loop cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Dispatcher loop encountered an error");
        }

        _logger?.LogDebug("Dispatcher loop ended");
    }

    private async Task DispatchMessageAsync(OutboundMessage message)
    {
        Func<OutboundMessage, Task>? callback;
        lock (_lock)
        {
            _outboundSubscribers.TryGetValue(message.Channel, out callback);
        }

        if (callback == null)
        {
            _logger?.LogWarning("No subscriber found for channel {Channel}", message.Channel);
            return;
        }

        try
        {
            await callback(message);
            _logger?.LogDebug("Dispatched message to channel {Channel}", message.Channel);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error dispatching message to channel {Channel}", message.Channel);
        }
    }

    public void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        _outboundChannel.Writer.TryComplete();
        _inboundChannel.Writer.TryComplete();
        _dispatcherCts?.Cancel();

        _logger?.LogInformation("Message bus stopped");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();

        // Wait for dispatcher task to complete before disposing CTS
        if (_dispatcherTask != null)
        {
            try
            {
                if (!_dispatcherTask.IsCompleted)
                {
                    _dispatcherTask.Wait(TimeSpan.FromSeconds(5));
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                // Expected when stopping
            }
        }

        _dispatcherCts?.Dispose();

        lock (_lock)
        {
            _outboundSubscribers.Clear();
        }

        _disposed = true;
        _logger?.LogInformation("Message bus disposed");
    }
}
