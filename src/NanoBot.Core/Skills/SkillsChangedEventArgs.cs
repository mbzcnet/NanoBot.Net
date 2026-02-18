namespace NanoBot.Core.Skills;

public class SkillsChangedEventArgs : EventArgs
{
    public IReadOnlyList<Skill> Added { get; init; } = Array.Empty<Skill>();

    public IReadOnlyList<Skill> Removed { get; init; } = Array.Empty<Skill>();

    public IReadOnlyList<Skill> Modified { get; init; } = Array.Empty<Skill>();
}
