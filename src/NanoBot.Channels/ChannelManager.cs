using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Bus;
using NanoBot.Core.Channels;

namespace NanoBot.Channels;

public class ChannelManager : IChannelManager, IDisposable
{
    private readonly ConcurrentDictionary<string, IChannel> _channels = new();
    private readonly IMessageBus _bus;
    private readonly ILogger<ChannelManager> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _dispatchTask;
    private bool _disposed;

    public IReadOnlyList<string> EnabledChannels => _channels.Keys.ToList();

    public event EventHandler<InboundMessage>? MessageReceived;

    public ChannelManager(IMessageBus bus, ILogger<ChannelManager> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    public void Register(IChannel channel)
    {
        if (_channels.TryAdd(channel.Id, channel))
        {
            channel.MessageReceived += OnChannelMessageReceived;
            _logger.LogInformation("Channel registered: {Id} ({Type})", channel.Id, channel.Type);
        }
        else
        {
            _logger.LogWarning("Channel already registered: {Id}", channel.Id);
        }
    }

    public IChannel? GetChannel(string id) => _channels.TryGetValue(id, out var channel) ? channel : null;

    public IReadOnlyList<IChannel> GetChannelsByType(string type) =>
        _channels.Values.Where(c => c.Type.Equals(type, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<IChannel> GetAllChannels() => _channels.Values.ToList();

    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        if (_channels.IsEmpty)
        {
            _logger.LogWarning("No channels to start");
            return;
        }

        _dispatchTask = DispatchOutboundAsync(_cts.Token);

        var tasks = _channels.Values.Select(async channel =>
        {
            try
            {
                _logger.LogInformation("Starting channel: {Id}", channel.Id);
                await channel.StartAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start channel: {Id}", channel.Id);
            }
        });

        await Task.WhenAll(tasks);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping all channels...");

        _cts.Cancel();

        if (_dispatchTask != null)
        {
            try
            {
                await _dispatchTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (var channel in _channels.Values)
        {
            try
            {
                await channel.StopAsync(cancellationToken);
                _logger.LogInformation("Stopped channel: {Id}", channel.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping channel: {Id}", channel.Id);
            }
        }
    }

    public IDictionary<string, ChannelStatus> GetStatus() =>
        _channels.ToDictionary(
            kvp => kvp.Key,
            kvp => new ChannelStatus { Enabled = true, Running = kvp.Value.IsConnected }
        );

    private void OnChannelMessageReceived(object? sender, InboundMessage message)
    {
        MessageReceived?.Invoke(this, message);
    }

    private async Task DispatchOutboundAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Outbound dispatcher started");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var message = await _bus.ConsumeOutboundAsync(cancellationToken);

                var channel = GetChannel(message.Channel);
                if (channel != null)
                {
                    try
                    {
                        await channel.SendMessageAsync(message, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending to channel: {Channel}", message.Channel);
                    }
                }
                else
                {
                    _logger.LogWarning("Unknown channel: {Channel}", message.Channel);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in outbound dispatcher");
                await Task.Delay(1000, cancellationToken);
            }
        }

        _logger.LogInformation("Outbound dispatcher stopped");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
