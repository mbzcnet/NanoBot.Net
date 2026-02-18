namespace NanoBot.Core.Bus;

public record BusMessage
{
    public required string Id { get; init; }

    public required BusMessageType Type { get; init; }

    public required string Content { get; init; }

    public string? SourceChannelId { get; init; }

    public string? TargetChannelId { get; init; }

    public string? SessionId { get; init; }

    public string? UserId { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public IDictionary<string, object>? Metadata { get; init; }
}
