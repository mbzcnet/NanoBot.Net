using NanoBot.Core.Workspace;

namespace NanoBot.Infrastructure.Workspace;

public class BootstrapLoader : IBootstrapLoader
{
    private readonly IWorkspaceManager _workspaceManager;

    public IReadOnlyList<string> BootstrapFiles { get; } = new List<string>
    {
        "AGENTS.md",
        "SOUL.md",
        "USER.md",
        "TOOLS.md"
    }.AsReadOnly();

    public BootstrapLoader(IWorkspaceManager workspaceManager)
    {
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
    }

    public async Task<string> LoadAllBootstrapFilesAsync(CancellationToken cancellationToken = default)
    {
        var parts = new List<string>();

        foreach (var fileName in BootstrapFiles)
        {
            var content = await LoadBootstrapFileAsync(fileName, cancellationToken);
            if (!string.IsNullOrEmpty(content))
            {
                parts.Add($"## {fileName}\n\n{content}");
            }
        }

        return string.Join("\n\n", parts);
    }

    public async Task<string?> LoadBootstrapFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePathForBootstrapFile(fileName);

        if (!File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(filePath, cancellationToken);
    }

    public Task<string?> LoadAgentsAsync(CancellationToken cancellationToken = default)
    {
        return LoadBootstrapFileAsync("AGENTS.md", cancellationToken);
    }

    public Task<string?> LoadSoulAsync(CancellationToken cancellationToken = default)
    {
        return LoadBootstrapFileAsync("SOUL.md", cancellationToken);
    }

    public Task<string?> LoadToolsAsync(CancellationToken cancellationToken = default)
    {
        return LoadBootstrapFileAsync("TOOLS.md", cancellationToken);
    }

    public Task<string?> LoadUserAsync(CancellationToken cancellationToken = default)
    {
        return LoadBootstrapFileAsync("USER.md", cancellationToken);
    }

    public Task<string?> LoadHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        return LoadBootstrapFileAsync("HEARTBEAT.md", cancellationToken);
    }

    private string GetFilePathForBootstrapFile(string fileName)
    {
        return fileName switch
        {
            "AGENTS.md" => _workspaceManager.GetAgentsFile(),
            "SOUL.md" => _workspaceManager.GetSoulFile(),
            "TOOLS.md" => _workspaceManager.GetToolsFile(),
            "USER.md" => _workspaceManager.GetUserFile(),
            "HEARTBEAT.md" => _workspaceManager.GetHeartbeatFile(),
            _ => Path.Combine(_workspaceManager.GetWorkspacePath(), fileName)
        };
    }
}
