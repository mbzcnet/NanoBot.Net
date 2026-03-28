namespace NanoBot.WebUI.Services;

/// <summary>
/// Agent config file paths used across the WebUI project.
/// </summary>
public static class ConfigPaths
{
    /// <summary>
    /// Returns the path to the agent config file (~/.nbot/config.json).
    /// </summary>
    public static string GetAgentConfigPath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".nbot", "config.json");
    }
}
