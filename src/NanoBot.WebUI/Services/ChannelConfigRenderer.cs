using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MudBlazor;
using NanoBot.Core.Configuration;
using NanoBot.WebUI.Components.Channels;

namespace NanoBot.WebUI.Services;

/// <summary>
/// Provides <see cref="RenderFragment"/> factories for channel config editor components.
/// </summary>
public static class ChannelConfigRenderer
{
    /// <summary>
    /// Returns a <see cref="RenderFragment"/> that renders the appropriate config editor
    /// for the given channel ID, wired with <paramref name="onChanged"/>.
    /// </summary>
    public static RenderFragment Render(string channelId, object config, Action<object> onChanged)
    {
        return channelId.ToLowerInvariant() switch
        {
            "telegram" => RenderEditor<TelegramConfigEditor, TelegramConfig>(config, onChanged),
            "discord" => RenderEditor<DiscordConfigEditor, DiscordConfig>(config, onChanged),
            "whatsapp" => RenderEditor<WhatsAppConfigEditor, WhatsAppConfig>(config, onChanged),
            "slack" => RenderEditor<SlackConfigEditor, SlackConfig>(config, onChanged),
            "feishu" => RenderEditor<FeishuConfigEditor, FeishuConfig>(config, onChanged),
            "dingtalk" => RenderEditor<DingTalkConfigEditor, DingTalkConfig>(config, onChanged),
            "email" => RenderEditor<EmailConfigEditor, EmailConfig>(config, onChanged),
            "qq" => RenderEditor<QQConfigEditor, QQConfig>(config, onChanged),
            "matrix" => RenderEditor<MatrixConfigEditor, MatrixConfig>(config, onChanged),
            "mo" or "mochat" => RenderEditor<MochatConfigEditor, MochatConfig>(config, onChanged),
            _ => RenderUnknown(channelId)
        };
    }

    private static RenderFragment RenderEditor<TEditor, TConfig>(object config, Action<object> onChanged)
        where TEditor : IComponent
        where TConfig : class
    {
        if (config is TConfig typed)
        {
            return builder =>
            {
                builder.OpenComponent<TEditor>(0);
                builder.AddAttribute(1, "Config", typed);
                builder.AddAttribute(2, "ConfigChanged",
                    EventCallback.Factory.Create<TConfig>(null!, c => onChanged(c)));
                builder.CloseComponent();
            };
        }
        return RenderMismatch();
    }

    private static RenderFragment RenderUnknown(string channelId)
    {
        return builder =>
        {
            builder.OpenComponent<MudAlert>(0);
            builder.AddAttribute(1, "Severity", Severity.Warning);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(b2 =>
            {
                b2.AddContent(0, $"未知通道类型: {channelId}");
            }));
            builder.CloseComponent();
        };
    }

    private static RenderFragment RenderMismatch()
    {
        return builder =>
        {
            builder.OpenComponent<MudAlert>(0);
            builder.AddAttribute(1, "Severity", Severity.Error);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(b2 =>
            {
                b2.AddContent(0, "配置类型不匹配");
            }));
            builder.CloseComponent();
        };
    }
}
