namespace NanoBot.Core.Skills;

/// <summary>
/// Provides skill loading and access capabilities.
/// </summary>
public interface ISkillsProvider
{
    /// <summary>
    /// Loads skills from the specified directory and embedded resources.
    /// </summary>
    Task<IReadOnlyList<Skill>> LoadAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all currently loaded skills.
    /// </summary>
    IReadOnlyList<Skill> GetLoadedSkills();

    /// <summary>
    /// Loads a single skill by name.
    /// </summary>
    Task<Skill?> LoadSkillAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads skill contents for a list of skill names.
    /// </summary>
    Task<string> LoadSkillsForContextAsync(IReadOnlyList<string> skillNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all skills with optional filtering.
    /// </summary>
    IReadOnlyList<SkillSummary> ListSkills(bool filterUnavailable = true);

    /// <summary>
    /// Gets the list of skills that should always be loaded.
    /// </summary>
    IReadOnlyList<string> GetAlwaysSkills();

    /// <summary>
    /// Builds a summary of all skills in XML format.
    /// </summary>
    Task<string> BuildSkillsSummaryAsync(CancellationToken cancellationToken = default);
}
