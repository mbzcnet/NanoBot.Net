namespace NanoBot.Core.Bus;

public record InboundMessage
{
    public required string Channel { get; init; }

    public required string SenderId { get; init; }

    public required string ChatId { get; init; }

    public required string Content { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<string> Media { get; init; } = Array.Empty<string>();

    public IDictionary<string, object>? Metadata { get; init; }

    public string SessionKey => $"{Channel}:{ChatId}";
}
