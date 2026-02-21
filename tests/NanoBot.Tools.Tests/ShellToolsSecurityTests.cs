using Microsoft.Extensions.AI;
using NanoBot.Tools.BuiltIn;
using Xunit;

namespace NanoBot.Tools.Tests;

public class ShellToolsSecurityTests
{
    [Fact]
    public void CreateExecTool_WithNullBlockedCommands_ReturnsAITool()
    {
        var tool = ShellTools.CreateExecTool((IEnumerable<string>?)null);

        Assert.NotNull(tool);
        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void DefaultDenyPatterns_ContainsDangerousCommands()
    {
        var patterns = ShellToolOptions.DefaultDenyPatterns;

        Assert.NotEmpty(patterns);
        Assert.Contains(patterns, p => p.Contains("rm"));
        Assert.Contains(patterns, p => p.Contains("del"));
        Assert.Contains(patterns, p => p.Contains("format"));
        Assert.Contains(patterns, p => p.Contains("shutdown"));
    }

    [Fact]
    public void ShellToolOptions_DefaultDenyPatterns_BlocksRmRf()
    {
        var patterns = ShellToolOptions.DefaultDenyPatterns;
        var rmPattern = patterns.First(p => p.Contains("rm"));

        Assert.Matches(rmPattern, "rm -rf /");
        Assert.Matches(rmPattern, "rm -r /home");
        Assert.DoesNotMatch(rmPattern, "rmdir");
    }

    [Fact]
    public void ShellToolOptions_DefaultDenyPatterns_BlocksFormatAsCommand()
    {
        var patterns = ShellToolOptions.DefaultDenyPatterns;
        var formatPattern = patterns.First(p => p.Contains("format"));

        Assert.Matches(formatPattern, "format");
        Assert.Matches(formatPattern, "format D:");
        Assert.Matches(formatPattern, "rm && format");
        Assert.DoesNotMatch(formatPattern, "curl https://wttr.in?format=3");
    }

    [Fact]
    public void ShellToolOptions_DefaultDenyPatterns_BlocksDangerousCommands()
    {
        var patterns = ShellToolOptions.DefaultDenyPatterns;

        var shutdownPattern = patterns.First(p => p.Contains("shutdown"));
        Assert.Matches(shutdownPattern, "shutdown -h now");

        var forkBombPattern = patterns.First(p => p.Contains(":\\(\\)\\s*\\{"));
        Assert.Matches(forkBombPattern, ":(){ :|:& };:");
    }

    [Fact]
    public void ShellToolOptions_DefaultDenyPatterns_BlocksDdCommand()
    {
        var patterns = ShellToolOptions.DefaultDenyPatterns;

        var ddPattern = patterns.First(p => p.Contains("dd"));
        Assert.Matches(ddPattern, "dd if=/dev/zero of=/dev/sda");
    }
}

public class ShellToolsExecutionTests
{
    [Fact]
    public async Task ExecTool_ReturnsToolWithCorrectName()
    {
        var tool = ShellTools.CreateExecTool((IEnumerable<string>?)null);

        Assert.Equal("exec", tool.Name);
    }

    [Fact]
    public async Task ExecTool_ReturnsToolWithDescription()
    {
        var tool = ShellTools.CreateExecTool((IEnumerable<string>?)null);

        Assert.Contains("shell", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecTool_WithBlockedCommands_BlocksSpecificCommands()
    {
        var blocked = new[] { "powershell", "cmd" };
        var tool = ShellTools.CreateExecTool(blocked);
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["command"] = "powershell -Command 'ls'",
                ["timeoutSeconds"] = 5,
                ["workingDir"] = null
            },
            CancellationToken.None);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("blocked", resultStr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecTool_EchoCommand_ReturnsOutput()
    {
        var tool = ShellTools.CreateExecTool((IEnumerable<string>?)null);
        var func = (AIFunction)tool;

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["command"] = "echo hello",
                ["timeoutSeconds"] = 5,
                ["workingDir"] = null
            },
            CancellationToken.None);

        var resultStr = result?.ToString() ?? "";
        Assert.Contains("hello", resultStr);
    }
}

public class ShellToolsOutputTruncationTests
{
    [Fact]
    public async Task ExecTool_TruncatesLongOutput()
    {
        var tool = ShellTools.CreateExecTool((IEnumerable<string>?)null);
        var func = (AIFunction)tool;

        var longCommand = "seq 1 20000";
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            longCommand = "for /L %i in (1,1,20000) do echo %i";
        }

        var result = await func.InvokeAsync(
            new AIFunctionArguments
            {
                ["command"] = longCommand,
                ["timeoutSeconds"] = 10,
                ["workingDir"] = null
            },
            CancellationToken.None);

        var resultStr = result?.ToString() ?? "";
        Assert.True(resultStr.Length <= 10500, $"Output should be truncated, but got {resultStr.Length} chars");
        Assert.Contains("truncated", resultStr, StringComparison.OrdinalIgnoreCase);
    }
}
