namespace NanoBot.Core.Configuration;

public class MochatConfig
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://mochat.io";
    public string SocketUrl { get; set; } = string.Empty;
    public string SocketPath { get; set; } = "/socket.io";
    public string ClawToken { get; set; } = string.Empty;
    public string AgentUserId { get; set; } = string.Empty;
    public IReadOnlyList<string> Sessions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Panels { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> AllowFrom { get; set; } = Array.Empty<string>();
    public MochatMentionConfig Mention { get; set; } = new();
    public string ReplyDelayMode { get; set; } = "non-mention";
    public int ReplyDelayMs { get; set; } = 120000;
}

public class MochatMentionConfig
{
    public bool RequireInGroups { get; set; }
}
