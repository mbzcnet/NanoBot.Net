namespace NanoBot.Core.Configuration;

public class TelegramConfig
{
    public bool Enabled { get; set; }
    public string Token { get; set; } = string.Empty;
    public IReadOnlyList<string> AllowFrom { get; set; } = Array.Empty<string>();
    public string? Proxy { get; set; }
    public bool ReplyToMessage { get; set; }

    // Connection pool settings (aligned with Python nanobot)
    public int ConnectionPoolSize { get; set; } = 32;
    public double PoolTimeout { get; set; } = 30.0;
    public int PollingPoolSize { get; set; } = 4;
    public double PollingTimeout { get; set; } = 60.0;

    // Media download settings
    public int MediaDownloadTimeout { get; set; } = 30;
    public int MaxMediaFileSizeMb { get; set; } = 20;
}
