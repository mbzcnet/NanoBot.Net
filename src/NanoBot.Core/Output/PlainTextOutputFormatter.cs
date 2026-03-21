namespace NanoBot.Core.Output;

/// <summary>
/// 纯文本输出格式化器（默认实现）
/// </summary>
public sealed class PlainTextOutputFormatter : IOutputFormatter
{
    public OutputFormatterType Type => OutputFormatterType.PlainText;

    public string Format(string content, OutputContext? context = null)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content ?? string.Empty;
        }

        return content;
    }
}
