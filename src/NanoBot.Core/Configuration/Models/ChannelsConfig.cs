namespace NanoBot.Core.Configuration;

public class ChannelsConfig
{
    /// <summary>
    /// Maximum retry attempts for failed message sends (default: 3)
    /// </summary>
    public int SendMaxRetries { get; set; } = 3;

    public bool SendProgress { get; set; } = true;
    public bool SendToolHints { get; set; } = false;

    public TelegramConfig? Telegram { get; set; }
    public DiscordConfig? Discord { get; set; }
    public FeishuConfig? Feishu { get; set; }
    public WhatsAppConfig? WhatsApp { get; set; }
    public DingTalkConfig? DingTalk { get; set; }
    public EmailConfig? Email { get; set; }
    public SlackConfig? Slack { get; set; }
    public QQConfig? QQ { get; set; }
    public MochatConfig? Mochat { get; set; }
    public MatrixConfig? Matrix { get; set; }
}

public class MatrixConfig
{
    public bool Enabled { get; set; } = false;

    public string? Homeserver { get; set; }

    public string? AccessToken { get; set; }

    public string? UserId { get; set; }

    public string? DeviceId { get; set; }

    public string? RoomId { get; set; }

    public bool EncryptRooms { get; set; } = false;

    public bool SyncPresence { get; set; } = true;
}
