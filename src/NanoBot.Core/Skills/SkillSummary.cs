namespace NanoBot.Core.Skills;

/// <summary>
/// Summary information for a skill, including availability status.
/// Inherits base properties from <see cref="Skill"/>.
/// </summary>
public record SkillSummary : Skill
{
    /// <summary>
    /// Whether the skill is currently available (all requirements met).
    /// </summary>
    public bool Available { get; init; }

    /// <summary>
    /// Description of missing requirements if not available.
    /// </summary>
    public string? MissingRequirements { get; init; }

    /// <summary>
    /// Creates a SkillSummary from a Skill with additional availability info.
    /// </summary>
    public static SkillSummary FromSkill(Skill skill, bool available = true, string? missingRequirements = null)
    {
        return new SkillSummary
        {
            Name = skill.Name,
            Description = skill.Description,
            Content = skill.Content,
            FilePath = skill.FilePath,
            Source = skill.Source,
            LoadedAt = skill.LoadedAt,
            Available = available,
            MissingRequirements = missingRequirements
        };
    }
}
