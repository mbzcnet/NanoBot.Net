namespace NanoBot.Core.Configuration;

public class DingTalkConfig
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public IReadOnlyList<string> AllowFrom { get; set; } = Array.Empty<string>();
}
