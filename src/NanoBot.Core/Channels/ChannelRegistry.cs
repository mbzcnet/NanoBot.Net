using NanoBot.Core.Channels;

namespace NanoBot.Core.Channels;

public interface IChannelRegistry
{
    ChannelUiCatalog GetCatalog();
    ChannelMeta? GetChannelMeta(string id);
    List<ChannelMeta> ListChannels();
}

public class ChannelRegistry : IChannelRegistry
{
    private readonly Dictionary<string, ChannelMeta> _channels;

    public ChannelRegistry()
    {
        _channels = new Dictionary<string, ChannelMeta>
        {
            ["telegram"] = new ChannelMeta
            {
                Id = "telegram",
                Label = "Telegram",
                SelectionLabel = "Telegram (Bot API)",
                DetailLabel = "Telegram Bot",
                DocsPath = "/channels/telegram",
                Blurb = "最简单的开始方式 - 通过 @BotFather 注册 bot 并开始使用",
                SystemImage = "paperplane",
                Order = 1
            },
            ["whatsapp"] = new ChannelMeta
            {
                Id = "whatsapp",
                Label = "WhatsApp",
                SelectionLabel = "WhatsApp (QR link)",
                DetailLabel = "WhatsApp Web",
                DocsPath = "/channels/whatsapp",
                Blurb = "使用你自己的号码；建议使用单独的手机 + eSIM",
                SystemImage = "message",
                Order = 2
            },
            ["discord"] = new ChannelMeta
            {
                Id = "discord",
                Label = "Discord",
                SelectionLabel = "Discord (Bot API)",
                DetailLabel = "Discord Bot",
                DocsPath = "/channels/discord",
                Blurb = "目前支持良好",
                SystemImage = "bubble.left.and.bubble.right",
                Order = 3
            },
            ["slack"] = new ChannelMeta
            {
                Id = "slack",
                Label = "Slack",
                SelectionLabel = "Slack (Socket Mode)",
                DetailLabel = "Slack Bot",
                DocsPath = "/channels/slack",
                Blurb = "支持 Socket Mode",
                SystemImage = "number",
                Order = 4
            },
            ["feishu"] = new ChannelMeta
            {
                Id = "feishu",
                Label = "飞书",
                SelectionLabel = "飞书 (Bot API)",
                DetailLabel = "飞书 Bot",
                DocsPath = "/channels/feishu",
                Blurb = "飞书企业应用",
                SystemImage = "work",
                Order = 5
            },
            ["dingtalk"] = new ChannelMeta
            {
                Id = "dingtalk",
                Label = "钉钉",
                SelectionLabel = "钉钉 (Bot API)",
                DetailLabel = "钉钉 Bot",
                DocsPath = "/channels/dingtalk",
                Blurb = "钉钉企业应用",
                SystemImage = "work",
                Order = 6
            },
            ["email"] = new ChannelMeta
            {
                Id = "email",
                Label = "Email",
                SelectionLabel = "Email (SMTP/IMAP)",
                DetailLabel = "Email",
                DocsPath = "/channels/email",
                Blurb = "通过电子邮件与 AI 交互",
                SystemImage = "mail",
                Order = 7
            },
            ["qq"] = new ChannelMeta
            {
                Id = "qq",
                Label = "QQ",
                SelectionLabel = "QQ (Bot API)",
                DetailLabel = "QQ Bot",
                DocsPath = "/channels/qq",
                Blurb = "QQ 机器人",
                SystemImage = "chat",
                Order = 8
            },
            ["matrix"] = new ChannelMeta
            {
                Id = "matrix",
                Label = "Matrix",
                SelectionLabel = "Matrix (Client API)",
                DetailLabel = "Matrix",
                DocsPath = "/channels/matrix",
                Blurb = "去中心化即时通讯",
                SystemImage = "network",
                Order = 9
            }
        };
    }

    public ChannelUiCatalog GetCatalog()
    {
        var entries = _channels.Values
            .OrderBy(c => c.Order ?? 999)
            .ThenBy(c => c.Label)
            .Select(meta => new ChannelUiMetaEntry
            {
                Id = meta.Id,
                Label = meta.Label,
                DetailLabel = meta.DetailLabel,
                SystemImage = meta.SystemImage
            })
            .ToList();

        var order = entries.Select(e => e.Id).ToList();
        var labels = entries.ToDictionary(e => e.Id, e => e.Label);
        var detailLabels = entries.ToDictionary(e => e.Id, e => e.DetailLabel);
        var systemImages = entries.Where(e => e.SystemImage != null)
            .ToDictionary(e => e.Id, e => e.SystemImage!);
        var byId = entries.ToDictionary(e => e.Id);

        return new ChannelUiCatalog
        {
            Entries = entries,
            Order = order,
            Labels = labels,
            DetailLabels = detailLabels,
            SystemImages = systemImages,
            ById = byId
        };
    }

    public ChannelMeta? GetChannelMeta(string id)
    {
        return _channels.GetValueOrDefault(id);
    }

    public List<ChannelMeta> ListChannels()
    {
        return _channels.Values
            .OrderBy(c => c.Order ?? 999)
            .ThenBy(c => c.Label)
            .ToList();
    }
}
