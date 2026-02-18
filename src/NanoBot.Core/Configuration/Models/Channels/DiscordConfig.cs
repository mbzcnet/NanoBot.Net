namespace NanoBot.Core.Configuration;

public class DiscordConfig
{
    public bool Enabled { get; set; }
    public string Token { get; set; } = string.Empty;
    public IReadOnlyList<string> AllowFrom { get; set; } = Array.Empty<string>();
    public string GatewayUrl { get; set; } = "wss://gateway.discord.gg/?v=10&encoding=json";
    public int Intents { get; set; } = 37377;
}
