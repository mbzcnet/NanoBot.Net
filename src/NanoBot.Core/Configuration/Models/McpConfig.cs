namespace NanoBot.Core.Configuration;

public class McpConfig
{
    public Dictionary<string, McpServerConfig> Servers { get; set; } = new();
}
