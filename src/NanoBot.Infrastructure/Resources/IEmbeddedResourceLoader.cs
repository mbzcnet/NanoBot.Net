namespace NanoBot.Infrastructure.Resources;

public interface IEmbeddedResourceLoader
{
    IReadOnlyList<string> GetWorkspaceResourceNames();

    IReadOnlyList<string> GetSkillsResourceNames();

    Task<string?> ReadResourceAsync(string resourceName, CancellationToken cancellationToken = default);

    Task ExtractAllResourcesAsync(string targetDirectory, CancellationToken cancellationToken = default);

    Task ExtractWorkspaceResourcesAsync(string targetDirectory, CancellationToken cancellationToken = default);

    Task ExtractSkillsResourcesAsync(string targetDirectory, CancellationToken cancellationToken = default);
}
