using NanoBot.Core.Output;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NanoBot.Cli.Formatting;

/// <summary>
/// 基于 Spectre.Console 的终端 ANSI 输出格式化器
/// </summary>
public sealed class SpectreOutputFormatter : IOutputFormatter
{
    public OutputFormatterType Type => OutputFormatterType.Console;

    public string Format(string content, OutputContext? context = null)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content ?? string.Empty;
        }

        if (context?.EnableMarkdown == false)
        {
            return content;
        }

        var processedContent = content;

        // 如果有图片上下文，在文本末尾追加图片链接
        if (context?.Images != null && context.Images.Count > 0)
        {
            processedContent = AppendImageMarkdown(processedContent, context.Images);
        }

        return processedContent;
    }

    /// <summary>
    /// 渲染 Markdown 内容到终端
    /// </summary>
    public void RenderToConsole(string content, OutputContext? context = null)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        var processedContent = Format(content, context);

        if (context?.EnableMarkdown == false)
        {
            AnsiConsole.WriteLine(processedContent);
            return;
        }

        AnsiConsole.Write(new Markup(processedContent));
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// 追加图片 Markdown 到内容末尾
    /// </summary>
    private static string AppendImageMarkdown(string content, IReadOnlyList<ImageContext> images)
    {
        if (images.Count == 0)
        {
            return content;
        }

        var imageLines = new List<string>();
        foreach (var image in images)
        {
            imageLines.Add($"![{image.AltText}]({image.Url})");
        }

        var separator = string.IsNullOrWhiteSpace(content) ? "" : "\n\n";
        return $"{content}{separator}{string.Join("\n\n", imageLines)}";
    }
}
