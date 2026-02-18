namespace NanoBot.Core.Workspace;

public interface IBootstrapLoader
{
    Task<string> LoadAllBootstrapFilesAsync(CancellationToken cancellationToken = default);

    Task<string?> LoadBootstrapFileAsync(string fileName, CancellationToken cancellationToken = default);

    Task<string?> LoadAgentsAsync(CancellationToken cancellationToken = default);

    Task<string?> LoadSoulAsync(CancellationToken cancellationToken = default);

    Task<string?> LoadToolsAsync(CancellationToken cancellationToken = default);

    Task<string?> LoadUserAsync(CancellationToken cancellationToken = default);

    Task<string?> LoadHeartbeatAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<string> BootstrapFiles { get; }
}
