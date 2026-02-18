namespace NanoBot.Core.Skills;

public record SkillSummary
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string FilePath { get; init; }

    public required string Source { get; init; }

    public bool Available { get; init; }

    public string? MissingRequirements { get; init; }
}
