namespace NanoBot.Core.Configuration;

/// <summary>
/// Personal WeChat (微信) channel configuration.
/// </summary>
public class WeiXinConfig
{
    public bool Enabled { get; set; }

    /// <summary>
    /// iLink API base URL. Default: https://ilinkai.weixin.qq.com
    /// </summary>
    public string BaseUrl { get; set; } = "https://ilinkai.weixin.qq.com";

    /// <summary>
    /// CDN base URL for media upload/download. Default: https://novac2c.cdn.weixin.qq.com/c2c
    /// </summary>
    public string CdnBaseUrl { get; set; } = "https://novac2c.cdn.weixin.qq.com/c2c";

    /// <summary>
    /// Optional route tag for upstream request routing (SKRouteTag header).
    /// </summary>
    public string? RouteTag { get; set; }

    /// <summary>
    /// Bot token obtained via QR code login. If empty, QR login will be performed on startup.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// State directory for persisting login session. Default: ~/.nanobot/weixin/
    /// </summary>
    public string StateDir { get; set; } = string.Empty;

    /// <summary>
    /// Long-poll timeout in seconds. Default: 35
    /// </summary>
    public int PollTimeout { get; set; } = 35;

    /// <summary>
    /// List of allowed sender user IDs. Empty = deny all.
    /// </summary>
    public IReadOnlyList<string> AllowFrom { get; set; } = Array.Empty<string>();
}
