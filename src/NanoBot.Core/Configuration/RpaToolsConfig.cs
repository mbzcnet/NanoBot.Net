namespace NanoBot.Core.Configuration;

/// <summary>
/// RPA 工具配置
/// </summary>
public class RpaToolsConfig
{
    /// <summary>
    /// 启用 RPA 工具
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// OmniParser 安装路径
    /// </summary>
    public string? InstallPath { get; set; }

    /// <summary>
    /// OmniParser 服务端口
    /// </summary>
    public int ServicePort { get; set; } = 18999;

    /// <summary>
    /// 自动启动 OmniParser 服务
    /// </summary>
    public bool AutoStartService { get; set; } = true;

    /// <summary>
    /// 截图保存路径（调试用），null 表示不保存
    /// </summary>
    public string? ScreenshotPath { get; set; }

    /// <summary>
    /// OmniParser 截图优化选项
    /// </summary>
    public ScreenshotOptimizationConfig ScreenshotOptimization { get; set; } = new();
}

/// <summary>
/// 截图优化配置
/// </summary>
public class ScreenshotOptimizationConfig
{
    /// <summary>
    /// 最大边长（像素）
    /// </summary>
    public int MaxDimension { get; set; } = 1024;

    /// <summary>
    /// JPEG 质量（0-100）
    /// </summary>
    public int JpegQuality { get; set; } = 70;
}
