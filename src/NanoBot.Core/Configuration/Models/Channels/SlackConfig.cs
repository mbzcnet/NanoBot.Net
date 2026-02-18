namespace NanoBot.Core.Configuration;

public class SlackConfig
{
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "socket";
    public string BotToken { get; set; } = string.Empty;
    public string AppToken { get; set; } = string.Empty;
    public string GroupPolicy { get; set; } = "mention";
    public IReadOnlyList<string> GroupAllowFrom { get; set; } = Array.Empty<string>();
    public SlackDmConfig Dm { get; set; } = new();
}

public class SlackDmConfig
{
    public bool Enabled { get; set; } = true;
    public string Policy { get; set; } = "open";
    public IReadOnlyList<string> AllowFrom { get; set; } = Array.Empty<string>();
}
