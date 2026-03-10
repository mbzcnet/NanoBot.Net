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

        Assert.Equal("\n🔧 web_search(\"test query\")\n", result);
    }

    [Fact]
    public void FormatToolHint_SingleToolWithLongArgument_TruncatesArgument()
    {
        var longQuery = new string('a', 50);
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "web_search", new Dictionary<string, object?> { ["query"] = longQuery })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        Assert.Equal($"\n🔧 web_search(\"{longQuery[..40]}…\")\n", result);
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

        Assert.Equal("\n🔧 web_search(\"test\"), read_file(\"/path/to/file\")\n", result);
    }

    [Fact]
    public void FormatToolHint_ToolWithNoArguments_ReturnsToolNameOnly()
    {
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "get_time", new Dictionary<string, object?>())
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        Assert.Equal("\n🔧 get_time\n", result);
    }

    [Fact]
    public void FormatToolHint_ToolWithNonStringArgument_ReturnsToolNameOnly()
    {
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "calculate", new Dictionary<string, object?> { ["value"] = 42 })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        Assert.Equal("\n🔧 calculate\n", result);
    }

    [Fact]
    public void FormatToolHint_ToolWithEmptyStringArgument_ReturnsToolNameOnly()
    {
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "search", new Dictionary<string, object?> { ["query"] = "" })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        Assert.Equal("\n🔧 search\n", result);
    }

    [Fact]
    public void FormatToolHint_EmptyList_ReturnsEmptyString()
    {
        var toolCalls = Array.Empty<FunctionCallContent>();

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        Assert.Equal("\n🔧 \n", result);
    }

    [Fact]
    public void FormatToolHint_ExactlyFortyCharacters_NoTruncation()
    {
        var query = new string('a', 40);
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "search", new Dictionary<string, object?> { ["query"] = query })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        Assert.Equal($"\n🔧 search(\"{query}\")\n", result);
    }

    [Fact]
    public void FormatToolHint_FortyOneCharacters_Truncates()
    {
        var query = new string('a', 41);
        var toolCalls = new[]
        {
            new FunctionCallContent("call123", "search", new Dictionary<string, object?> { ["query"] = query })
        };

        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        Assert.Equal($"\n🔧 search(\"{query[..40]}…\")\n", result);
    }
}
