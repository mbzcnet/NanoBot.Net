namespace NanoBot.Core.Configuration;

public class WorkspaceConfig
{
    public string Path { get; set; } = ".nbot";

    public string GetResolvedPath()
    {
        string path = Path;
        if (path.StartsWith("~/"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = System.IO.Path.Combine(home, path[2..]);
        }
        return System.IO.Path.GetFullPath(path);
    }

    public string GetMemoryPath() => System.IO.Path.Combine(GetResolvedPath(), "memory");

    public string GetSkillsPath() => System.IO.Path.Combine(GetResolvedPath(), "skills");

    public string GetSessionsPath() => System.IO.Path.Combine(GetResolvedPath(), "sessions");

    public string GetAgentsFile() => System.IO.Path.Combine(GetResolvedPath(), "AGENTS.md");

    public string GetSoulFile() => System.IO.Path.Combine(GetResolvedPath(), "SOUL.md");

    public string GetToolsFile() => System.IO.Path.Combine(GetResolvedPath(), "TOOLS.md");

    public string GetUserFile() => System.IO.Path.Combine(GetResolvedPath(), "USER.md");

    public string GetHeartbeatFile() => System.IO.Path.Combine(GetResolvedPath(), "HEARTBEAT.md");

    public string GetMemoryFile() => System.IO.Path.Combine(GetResolvedPath(), "memory", "MEMORY.md");

    public string GetHistoryFile() => System.IO.Path.Combine(GetResolvedPath(), "memory", "HISTORY.md");
}
