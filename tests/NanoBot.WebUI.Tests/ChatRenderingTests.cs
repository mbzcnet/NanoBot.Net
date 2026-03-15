using System.Reflection;
using FluentAssertions;
using NanoBot.WebUI.Components.Pages;
using NanoBot.WebUI.Components.Shared;
using Xunit;

namespace NanoBot.WebUI.Tests;

public class ChatRenderingTests
{
    [Fact]
    public void ShouldRenderToolOutputAsMarkdown_WithImageMarkdown_ShouldReturnTrue()
    {
        var method = typeof(Chat).GetMethod("ShouldRenderToolOutputAsMarkdown", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, ["![snapshot](/api/files/sessions/a.png)"])!;

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRenderToolOutputAsMarkdown_WithMarkdownTable_ShouldReturnTrue()
    {
        var method = typeof(Chat).GetMethod("ShouldRenderToolOutputAsMarkdown", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        const string markdownTable = "| 列1 | 列2 |\n| --- | --- |\n| A | B |";
        var result = (bool)method!.Invoke(null, [markdownTable])!;

        result.Should().BeTrue();
    }

    [Fact]
    public void MarkdownRenderer_WithMarkdownTable_ShouldGenerateTableHtml()
    {
        var component = new MarkdownRenderer
        {
            Content = "| 列1 | 列2 |\n| --- | --- |\n| A | B |"
        };

        var onParametersSet = typeof(MarkdownRenderer).GetMethod("OnParametersSet", BindingFlags.NonPublic | BindingFlags.Instance);
        onParametersSet.Should().NotBeNull();
        onParametersSet!.Invoke(component, null);

        var field = typeof(MarkdownRenderer).GetField("_htmlContent", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        var html = field!.GetValue(component) as string;

        html.Should().NotBeNull();
        html.Should().Contain("<table>");
        html.Should().Contain("<td>A</td>");
    }
}
