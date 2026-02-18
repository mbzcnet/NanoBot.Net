namespace NanoBot.Core.Workspace;

public record BootstrapFile
{
    public required string FileName { get; init; }

    public required string Description { get; init; }

    public bool Required { get; init; }

    public string? DefaultContent { get; init; }
}
