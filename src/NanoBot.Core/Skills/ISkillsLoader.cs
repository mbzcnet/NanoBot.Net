namespace NanoBot.Core.Skills;

public interface ISkillsLoader
{
    Task<IReadOnlyList<Skill>> LoadAsync(string directory, CancellationToken cancellationToken = default);

    IReadOnlyList<Skill> GetLoadedSkills();

    Task ReloadAsync(CancellationToken cancellationToken = default);

    event EventHandler<SkillsChangedEventArgs>? SkillsChanged;

    IReadOnlyList<SkillSummary> ListSkills(bool filterUnavailable = true);

    Task<Skill?> LoadSkillAsync(string name, CancellationToken cancellationToken = default);

    Task<string> LoadSkillsForContextAsync(IReadOnlyList<string> skillNames, CancellationToken cancellationToken = default);

    Task<string> BuildSkillsSummaryAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<string> GetAlwaysSkills();

    Task<SkillMetadata?> GetSkillMetadataAsync(string name, CancellationToken cancellationToken = default);

    bool CheckRequirements(SkillMetadata metadata);

    string? GetMissingRequirements(SkillMetadata metadata);
}
