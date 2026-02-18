namespace NanoBot.Core.Configuration;

public class WhatsAppConfig
{
    public bool Enabled { get; set; }
    public string BridgeUrl { get; set; } = "ws://localhost:3001";
    public string BridgeToken { get; set; } = string.Empty;
    public IReadOnlyList<string> AllowFrom { get; set; } = Array.Empty<string>();
}
