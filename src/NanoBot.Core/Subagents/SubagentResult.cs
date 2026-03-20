namespace NanoBot.Core.Subagents;

public record SubagentResult
{
    public required string Id { get; init; }

    public required SubagentStatus Status { get; init; }

    public string? Output { get; init; }

    public string? Error { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Result message role (should always be "assistant" for subagent results)</summary>
    public string Role { get; init; } = "assistant";

    /// <summary>Validates that the role is correctly set to assistant</summary>
    public bool IsRoleValid => Role == "assistant";
}
