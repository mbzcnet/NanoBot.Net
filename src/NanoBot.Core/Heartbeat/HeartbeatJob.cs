namespace NanoBot.Core.Heartbeat;

public record HeartbeatJob
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required int IntervalSeconds { get; init; }

    public required string Message { get; init; }

    public string? ChannelId { get; init; }

    public string? ChatId { get; init; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset? LastRunAt { get; set; }

    public DateTimeOffset? NextRunAt { get; set; }
}
