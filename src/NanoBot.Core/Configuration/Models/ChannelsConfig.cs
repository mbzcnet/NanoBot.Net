namespace NanoBot.Core.Configuration;

public class ChannelsConfig
{
    public TelegramConfig? Telegram { get; set; }
    public DiscordConfig? Discord { get; set; }
    public FeishuConfig? Feishu { get; set; }
    public WhatsAppConfig? WhatsApp { get; set; }
    public DingTalkConfig? DingTalk { get; set; }
    public EmailConfig? Email { get; set; }
    public SlackConfig? Slack { get; set; }
    public QQConfig? QQ { get; set; }
    public MochatConfig? Mochat { get; set; }
}
