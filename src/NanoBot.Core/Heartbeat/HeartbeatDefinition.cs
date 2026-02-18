namespace NanoBot.Core.Heartbeat;

public record HeartbeatDefinition
{
    public required string Name { get; init; }

    public required int IntervalSeconds { get; init; }

    public required string Message { get; init; }

    public string? ChannelId { get; init; }

    public string? ChatId { get; init; }
}
