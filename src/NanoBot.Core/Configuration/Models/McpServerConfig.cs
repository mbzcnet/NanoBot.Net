namespace NanoBot.Core.Configuration;

public class McpServerConfig
{
    public string Command { get; set; } = string.Empty;

    public IReadOnlyList<string> Args { get; set; } = Array.Empty<string>();

    public Dictionary<string, string> Env { get; set; } = new();

    public string? Cwd { get; set; }

    public Dictionary<string, string>? Headers { get; set; }

    public int ToolTimeout { get; set; } = 30;

    /// <summary>
    /// List of enabled tools. ["*"] means all tools are enabled.
    /// Supports raw tool names and wrapped names (mcp_server_toolname).
    /// </summary>
    public List<string> EnabledTools { get; set; } = new();
}
