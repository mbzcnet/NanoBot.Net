namespace NanoBot.Core.Skills;

public record SkillMetadata
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public string? Homepage { get; init; }

    public bool Always { get; init; }

    public NanobotMetadata? Nanobot { get; init; }
}

public record NanobotMetadata
{
    public string? Emoji { get; init; }

    public RequirementsMetadata? Requires { get; init; }

    public List<InstallMetadata>? Install { get; init; }
}

public record RequirementsMetadata
{
    public List<string>? Bins { get; init; }

    public List<string>? Env { get; init; }
}

public record InstallMetadata
{
    public required string Id { get; init; }

    public required string Kind { get; init; }

    public required string Formula { get; init; }

    public List<string>? Bins { get; init; }

    public required string Label { get; init; }
}
