using Microsoft.Extensions.AI;
using Xunit;

namespace NanoBot.Agent.Tests;

public class ToolHintFormatterTests
{
    [Fact]
    public void FormatToolHint_SingleToolWithShortArgument_ReturnsFormattedHint()
    {
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "web_search", new Dictionary<string, object?> { ["query"] = "test query" })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        // Arguments with spaces get quoted
        Assert.Equal("[TOOL_CALL]web_search(query=\"test query\")[/TOOL_CALL]", result);
    }

    [Fact]
    public void FormatToolHint_SingleToolWithLongArgument_TruncatesArgument()
    {
        var longQuery = new string('a', 60);
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "web_search", new Dictionary<string, object?> { ["query"] = longQuery })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        // Long arguments are truncated to 50 chars (MaxArgumentLength)
        Assert.Contains("…", result);
        Assert.DoesNotContain(longQuery, result);
    }

    [Fact]
    public void FormatToolHint_MultipleTools_ReturnsCommaSeparated()
    {
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "web_search", new Dictionary<string, object?> { ["query"] = "test" }),
            new FunctionCallContent("call456", "read_file", new Dictionary<string, object?> { ["path"] = "/path/to/file" })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        // Multiple tools use ||| separator
        Assert.Contains("|||", result);
        Assert.Contains("web_search", result);
        Assert.Contains("read_file", result);
    }

    [Fact]
    public void FormatToolHint_ToolWithNoArguments_ReturnsToolNameOnly()
    {
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "get_time", new Dictionary<string, object?>())
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        Assert.Equal("[TOOL_CALL]get_time()[/TOOL_CALL]", result);
    }

    [Fact]
    public void FormatToolHint_ToolWithNonStringArgument_ReturnsFormattedArgument()
    {
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "calculate", new Dictionary<string, object?> { ["value"] = 42 })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        // Numbers are shown as key=value without quotes
        Assert.Equal("[TOOL_CALL]calculate(value=42)[/TOOL_CALL]", result);
    }

    [Fact]
    public void FormatToolHint_ToolWithEmptyStringArgument_ReturnsToolNameOnly()
    {
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "search", new Dictionary<string, object?> { ["query"] = "" })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        // Empty string argument is skipped, shows only tool name
        Assert.Equal("[TOOL_CALL]search()[/TOOL_CALL]", result);
    }

    [Fact]
    public void FormatToolHint_EmptyList_ReturnsEmptyString()
    {
        var toolCalls = Array.Empty<FunctionCallContent>();

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        Assert.Equal("[TOOL_CALL][/TOOL_CALL]", result);
    }

    [Fact]
    public void FormatToolHint_ShortArgument_NotTruncated()
    {
        var query = new string('a', 30);
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "search", new Dictionary<string, object?> { ["query"] = query })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        // Short arguments are not truncated
        Assert.DoesNotContain("…", result);
        Assert.Contains("search(query=aaaa", result);
    }

    [Fact]
    public void FormatToolHint_LongArgument_IsTruncated()
    {
        var query = new string('a', 60);
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "search", new Dictionary<string, object?> { ["query"] = query })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        // Long arguments are truncated with ellipsis
        Assert.Contains("…", result);
        Assert.DoesNotContain("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result);
    }
}
