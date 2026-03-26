namespace NanoBot.Core.Configuration;

public class AgentConfig
{
    public string Name { get; set; } = "NanoBot";

    /// <summary>
    /// Runtime timezone (IANA format, e.g., "Asia/Shanghai", "UTC")
    /// </summary>
    public string? Timezone { get; set; }

    public WorkspaceConfig Workspace { get; set; } = new();

    public LlmConfig Llm { get; set; } = new();

    public ChannelsConfig Channels { get; set; } = new();

    public McpConfig? Mcp { get; set; }

    public SecurityConfig Security { get; set; } = new();

    public MemoryConfig Memory { get; set; } = new();

    public HeartbeatConfig? Heartbeat { get; set; }

    public WebUIConfig WebUI { get; set; } = new();

    public WebToolsConfig? WebTools { get; set; }

    public FileToolsConfig? FileTools { get; set; }

    public BrowserToolsConfig? Browser { get; set; }

    /// <summary>
    /// RPA 工具配置
    /// </summary>
    public RpaToolsConfig? Rpa { get; set; }
}

public class WebToolsConfig
{
    public string? SearchApikey { get; set; }

    public string? FetchUserAgent { get; set; } = "Mozilla/5.0 (compatible; NanoBot/1.0)";
}
