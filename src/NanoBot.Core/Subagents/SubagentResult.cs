namespace NanoBot.Core.Subagents;

public record SubagentResult
{
    public required string Id { get; init; }

    public required SubagentStatus Status { get; init; }

    public string? Output { get; init; }

    public string? Error { get; init; }

    public TimeSpan Duration { get; init; }
}
