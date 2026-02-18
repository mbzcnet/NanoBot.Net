namespace NanoBot.Core.Skills;

public record Skill
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public string? Content { get; init; }

    public string? FilePath { get; init; }

    public string Source { get; init; } = "workspace";

    public DateTimeOffset LoadedAt { get; init; }
}
