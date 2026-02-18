namespace NanoBot.Core.Configuration;

public class SecurityConfig
{
    public IReadOnlyList<string> AllowedDirs { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> DenyCommandPatterns { get; set; } = Array.Empty<string>();

    public bool RestrictToWorkspace { get; set; } = true;

    public int ShellTimeout { get; set; } = 60;
}
