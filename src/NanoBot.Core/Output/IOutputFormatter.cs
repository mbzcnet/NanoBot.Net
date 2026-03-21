namespace NanoBot.Core.Output;

/// <summary>
/// 输出格式化器接口，支持不同目标设备的差异化渲染
/// </summary>
public interface IOutputFormatter
{
    /// <summary>
    /// 格式化文本内容
    /// </summary>
    /// <param name="content">原始文本内容</param>
    /// <param name="context">格式化上下文，包含图片等元数据</param>
    /// <returns>格式化后的输出</returns>
    string Format(string content, OutputContext? context = null);

    /// <summary>
    /// 获取格式化器类型标识
    /// </summary>
    OutputFormatterType Type { get; }
}

/// <summary>
/// 输出格式化器类型
/// </summary>
public enum OutputFormatterType
{
    /// <summary>
    /// 终端 ANSI 输出
    /// </summary>
    Console,

    /// <summary>
    /// Web UI HTML 输出
    /// </summary>
    WebUI,

    /// <summary>
    /// Telegram HTML 输出
    /// </summary>
    Telegram,

    /// <summary>
    /// Discord Markdown 输出
    /// </summary>
    Discord,

    /// <summary>
    /// 飞书 Markdown 输出
    /// </summary>
    Feishu,

    /// <summary>
    /// 纯文本输出
    /// </summary>
    PlainText
}

/// <summary>
/// 输出格式化上下文，包含渲染所需的元数据
/// </summary>
public record OutputContext
{
    /// <summary>
    /// 是否启用 Markdown 渲染
    /// </summary>
    public bool EnableMarkdown { get; init; } = true;

    /// <summary>
    /// 图片上下文列表（用于在渲染时生成图片 Markdown）
    /// </summary>
    public IReadOnlyList<ImageContext>? Images { get; init; }
}

/// <summary>
/// 图片上下文，用于在渲染时生成图片 Markdown
/// </summary>
public record ImageContext(string Url, string AltText, int Index);
