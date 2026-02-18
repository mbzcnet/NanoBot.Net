namespace NanoBot.Core.Bus;

public record OutboundMessage
{
    public required string Channel { get; init; }

    public required string ChatId { get; init; }

    public required string Content { get; init; }

    public string? ReplyTo { get; init; }

    public IReadOnlyList<string> Media { get; init; } = Array.Empty<string>();

    public IDictionary<string, object>? Metadata { get; init; }
}
