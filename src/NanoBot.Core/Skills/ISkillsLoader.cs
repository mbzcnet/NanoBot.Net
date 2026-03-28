namespace NanoBot.Core.Skills;

/// <summary>
/// Skills loader interface - combines ISkillsProvider and ISkillsMetadataProvider.
/// </summary>
/// <remarks>
/// This interface is the composition of skill loading and metadata capabilities.
/// Use <see cref="ISkillsProvider"/> for skill access or <see cref="ISkillsMetadataProvider"/> for metadata operations.
/// </remarks>
public interface ISkillsLoader : ISkillsProvider, ISkillsMetadataProvider
{
    /// <summary>
    /// Reloads all skills from disk and embedded resources.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures skills are loaded. If already loaded, this is a no-op.
    /// Call this before accessing skills to ensure they are loaded.
    /// </summary>
    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when skills have changed.
    /// </summary>
    event EventHandler<SkillsChangedEventArgs>? SkillsChanged;
}
