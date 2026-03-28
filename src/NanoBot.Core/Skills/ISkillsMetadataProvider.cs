namespace NanoBot.Core.Skills;

/// <summary>
/// Provides skill metadata and requirement checking capabilities.
/// </summary>
public interface ISkillsMetadataProvider
{
    /// <summary>
    /// Gets metadata for a specific skill.
    /// </summary>
    Task<SkillMetadata?> GetSkillMetadataAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a skill's requirements are met.
    /// </summary>
    bool CheckRequirements(SkillMetadata metadata);

    /// <summary>
    /// Gets a human-readable description of missing requirements.
    /// </summary>
    string? GetMissingRequirements(SkillMetadata metadata);
}
