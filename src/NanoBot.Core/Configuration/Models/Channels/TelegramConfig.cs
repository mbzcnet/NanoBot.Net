namespace NanoBot.Core.Configuration;

public class TelegramConfig
{
    public bool Enabled { get; set; }
    public string Token { get; set; } = string.Empty;
    public IReadOnlyList<string> AllowFrom { get; set; } = Array.Empty<string>();
    public string? Proxy { get; set; }
    public bool ReplyToMessage { get; set; }
}
