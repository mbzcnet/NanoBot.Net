namespace NanoBot.Core.Subagents;

public record SubagentInfo
{
    public required string Id { get; init; }

    public required string Task { get; init; }

    public string? Label { get; init; }

    public required string OriginChannel { get; init; }

    public required string OriginChatId { get; init; }

    public SubagentStatus Status { get; set; } = SubagentStatus.Running;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; set; }
}
