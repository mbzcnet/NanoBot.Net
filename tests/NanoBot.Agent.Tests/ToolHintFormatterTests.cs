using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;
using NanoBot.Agent;

namespace NanoBot.Agent.Tests;

public class ToolHintFormatterTests
{
    [Fact]
    public void FormatToolHint_WithEmptyCollection_ReturnsEmptyMarker()
    {
        // Act
        var result = ToolHintFormatter.FormatToolHint(Array.Empty<FunctionCallContent>());

        // Assert
        Assert.Equal("[TOOL_CALL][/TOOL_CALL]", result);
    }

    [Fact]
    public void FormatToolHint_WithSingleCall_ReturnsFormattedHint()
    {
        // Arrange
        var toolCalls = new List<FunctionCallContent>
        {
            new FunctionCallContent("read_file", "read_file", null)
            {
                Arguments = new Dictionary<string, object?> { ["path"] = "/tmp/test.txt" }
            }
        };

        // Act
        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        // Assert
        Assert.Contains("[TOOL_CALL]", result);
        Assert.Contains("read_file", result);
        Assert.Contains("path=", result);
        Assert.Contains("[/TOOL_CALL]", result);
    }

    [Fact]
    public void FormatToolHint_WithMultipleCalls_ReturnsJoinedHints()
    {
        // Arrange
        var toolCalls = new List<FunctionCallContent>
        {
            new FunctionCallContent("read_file", "read_file", null),
            new FunctionCallContent("write_file", "write_file", null)
        };

        // Act
        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        // Assert
        Assert.Contains("read_file", result);
        Assert.Contains("write_file", result);
        Assert.Contains("|||", result);
    }

    [Fact]
    public void FormatToolHint_WithManyArguments_TruncatesAndShowsEllipsis()
    {
        // Arrange
        var toolCalls = new List<FunctionCallContent>
        {
            new FunctionCallContent("exec", "exec", null)
            {
                Arguments = new Dictionary<string, object?>
                {
                    ["cmd1"] = "value1",
                    ["cmd2"] = "value2",
                    ["cmd3"] = "value3"
                }
            }
        };

        // Act
        var result = ToolHintFormatter.FormatToolHint(toolCalls);

        // Assert - truncation uses Unicode ellipsis character
        Assert.Contains("…", result);
    }

    [Fact]
    public void GetToolDescription_WithReadFile_ReturnsReadingDescription()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["path"] = "/tmp/test.txt" };

        // Act
        var result = ToolHintFormatter.GetToolDescription("read_file", arguments);

        // Assert
        Assert.Contains("Reading", result);
        Assert.Contains("/tmp/test.txt", result);
    }

    [Fact]
    public void GetToolDescription_WithWriteFile_ReturnsWritingDescription()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["path"] = "/tmp/output.txt" };

        // Act
        var result = ToolHintFormatter.GetToolDescription("write_file", arguments);

        // Assert
        Assert.Contains("Writing", result);
    }

    [Fact]
    public void GetToolDescription_WithUnknownTool_ReturnsGenericDescription()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["param"] = "value" };

        // Act
        var result = ToolHintFormatter.GetToolDescription("unknown_tool", arguments);

        // Assert
        Assert.Contains("unknown_tool", result);
        Assert.Contains("Executing", result);
    }

    [Fact]
    public void GetToolDescription_WithNullArguments_ReturnsGenericDescription()
    {
        // Act
        var result = ToolHintFormatter.GetToolDescription("exec", null);

        // Assert
        Assert.Contains("Executing", result);
        Assert.Contains("exec", result);
    }

    [Fact]
    public void GetToolDescription_WithWebSearch_ReturnsSearchDescription()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["query"] = "test query" };

        // Act
        var result = ToolHintFormatter.GetToolDescription("web_search", arguments);

        // Assert
        Assert.Contains("Searching web", result);
        Assert.Contains("test query", result);
    }

    [Fact]
    public void GetToolDescription_WithBrowserOpen_ReturnsBrowserDescription()
    {
        // Arrange
        var arguments = new Dictionary<string, object?> { ["url"] = "https://example.com" };

        // Act
        var result = ToolHintFormatter.GetToolDescription("browser", arguments);

        // Assert
        Assert.Contains("Opening browser", result);
    }

    [Fact]
    public void FormatToolResult_WithErrorPayload_ReturnsErrorMessage()
    {
        // Arrange
        var functionResult = new FunctionResultContent(
            callId: "call-123",
            result: JsonSerializer.Serialize(new { error = "Something went wrong" })
        );

        // Act
        var result = ToolHintFormatter.FormatToolResult(functionResult);

        // Assert
        Assert.Contains("ERROR", result);
        Assert.Contains("Something went wrong", result);
    }

    [Fact]
    public void FormatToolResult_WithContentPayload_ReturnsTruncatedContent()
    {
        // Arrange
        var longContent = new string('x', 500);
        var functionResult = new FunctionResultContent(
            callId: "call-123",
            result: JsonSerializer.Serialize(new { content = longContent })
        );

        // Act
        var result = ToolHintFormatter.FormatToolResult(functionResult);

        // Assert - truncation uses Unicode ellipsis character
        Assert.NotNull(result);
        Assert.True(result!.Length < 500);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void FormatToolResult_WithSnapshotAction_ReturnsSnapshotMessage()
    {
        // Arrange
        var functionResult = new FunctionResultContent(
            callId: "call-123",
            result: JsonSerializer.Serialize(new
            {
                action = "snapshot",
                imagePath = "/tmp/screenshot.png"
            })
        );

        // Act
        var result = ToolHintFormatter.FormatToolResult(functionResult);

        // Assert
        Assert.Contains("snapshot", result);
        Assert.Contains("captured", result);
    }

    [Fact]
    public void FormatToolResult_WithNullResult_ReturnsNull()
    {
        // Arrange
        var functionResult = new FunctionResultContent(
            callId: "call-123",
            result: (string?)null
        );

        // Act
        var result = ToolHintFormatter.FormatToolResult(functionResult);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FormatToolResult_WithEmptyResult_ReturnsNull()
    {
        // Arrange
        var functionResult = new FunctionResultContent(
            callId: "call-123",
            result: "   "
        );

        // Act
        var result = ToolHintFormatter.FormatToolResult(functionResult);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetFunctionResultPayload_WithStringResult_ReturnsString()
    {
        // Arrange
        var functionResult = new FunctionResultContent(
            callId: "call-123",
            result: "plain text result"
        );

        // Act
        var result = ToolHintFormatter.GetFunctionResultPayload(functionResult);

        // Assert
        Assert.Equal("plain text result", result);
    }

    [Fact]
    public void GetFunctionResultPayload_WithJsonElement_ReturnsRawText()
    {
        // Arrange
        var jsonElement = JsonDocument.Parse("{\"key\": \"value\"}").RootElement;
        var functionResult = new FunctionResultContent(
            callId: "call-123",
            result: jsonElement
        );

        // Act
        var result = ToolHintFormatter.GetFunctionResultPayload(functionResult);

        // Assert
        Assert.Contains("key", result);
        Assert.Contains("value", result);
    }

    [Fact]
    public void TruncateValue_WithShortString_ReturnsOriginal()
    {
        // Arrange
        var shortValue = "short string";

        // Act
        var result = ToolHintFormatter.TruncateValue(shortValue, 50);

        // Assert
        Assert.Equal(shortValue, result);
    }

    [Fact]
    public void TruncateValue_WithLongString_TruncatesAndAppendsEllipsis()
    {
        // Arrange
        var longValue = new string('x', 100);

        // Act
        var result = ToolHintFormatter.TruncateValue(longValue, 50);

        // Assert - truncation uses Unicode ellipsis character
        Assert.Equal(51, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void WrapToolHintAsMarkdown_WrapsCorrectly()
    {
        // Arrange
        var toolHint = "[TOOL_CALL]read_file()[/TOOL_CALL]";

        // Act
        var result = ToolHintFormatter.WrapToolHintAsMarkdown(toolHint);

        // Assert
        Assert.Equal("\n[TOOL_CALL]read_file()[/TOOL_CALL]\n", result);
    }
}
