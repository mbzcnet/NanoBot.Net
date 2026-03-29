using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NanoBot.Channels;
using NanoBot.Core.Channels;
using NanoBot.Channels.Abstractions;

namespace NanoBot.Agent.Services;

public class ChannelStartupService : IHostedService
{
    private readonly IChannelManager _channelManager;
    private readonly ILogger<ChannelStartupService> _logger;

    public ChannelStartupService(IChannelManager channelManager, ILogger<ChannelStartupService> logger)
    {
        _channelManager = channelManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting channels...");
        await _channelManager.StartAllAsync(cancellationToken);
        _logger.LogInformation("Channels started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping channels...");
        await _channelManager.StopAllAsync(cancellationToken);
        _logger.LogInformation("Channels stopped successfully");
    }
}
