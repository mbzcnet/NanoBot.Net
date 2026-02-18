namespace NanoBot.Core.Configuration;

public class AgentConfig
{
    public string Name { get; set; } = "NanoBot";

    public WorkspaceConfig Workspace { get; set; } = new();

    public LlmConfig Llm { get; set; } = new();

    public ChannelsConfig Channels { get; set; } = new();

    public McpConfig? Mcp { get; set; }

    public SecurityConfig Security { get; set; } = new();

    public MemoryConfig Memory { get; set; } = new();

    public HeartbeatConfig? Heartbeat { get; set; }
}
