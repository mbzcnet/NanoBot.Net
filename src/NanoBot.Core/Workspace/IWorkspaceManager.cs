namespace NanoBot.Core.Workspace;

public interface IWorkspaceManager
{
    string GetWorkspacePath();

    string GetMemoryPath();

    string GetSkillsPath();

    string GetSessionsPath();

    string GetAgentsFile();

    string GetSoulFile();

    string GetToolsFile();

    string GetUserFile();

    string GetHeartbeatFile();

    string GetMemoryFile();

    string GetHistoryFile();

    Task InitializeAsync(CancellationToken cancellationToken = default);

    void EnsureDirectory(string path);

    bool FileExists(string relativePath);

    Task<string?> ReadFileAsync(string relativePath, CancellationToken cancellationToken = default);

    Task WriteFileAsync(string relativePath, string content, CancellationToken cancellationToken = default);

    Task AppendFileAsync(string relativePath, string content, CancellationToken cancellationToken = default);
}
