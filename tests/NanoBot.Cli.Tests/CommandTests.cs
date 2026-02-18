using NanoBot.Cli.Commands;
using Xunit;

namespace NanoBot.Cli.Tests;

public class CommandTests
{
    [Fact]
    public void OnboardCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new OnboardCommand();

        Assert.Equal("onboard", command.Name);
        Assert.Equal("Initialize nbot configuration and workspace", command.Description);
    }

    [Fact]
    public void AgentCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new AgentCommand();

        Assert.Equal("agent", command.Name);
        Assert.Equal("Start Agent interactive mode", command.Description);
    }

    [Fact]
    public void GatewayCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new GatewayCommand();

        Assert.Equal("gateway", command.Name);
        Assert.Equal("Start Gateway service mode", command.Description);
    }

    [Fact]
    public void StatusCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new StatusCommand();

        Assert.Equal("status", command.Name);
        Assert.Equal("Show Agent status", command.Description);
    }

    [Fact]
    public void ConfigCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ConfigCommand();

        Assert.Equal("config", command.Name);
        Assert.Equal("Configuration management", command.Description);
    }

    [Fact]
    public void SessionCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new SessionCommand();

        Assert.Equal("session", command.Name);
        Assert.Equal("Session management", command.Description);
    }

    [Fact]
    public void CronCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new CronCommand();

        Assert.Equal("cron", command.Name);
        Assert.Equal("Scheduled task management", command.Description);
    }

    [Fact]
    public void McpCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new McpCommand();

        Assert.Equal("mcp", command.Name);
        Assert.Equal("MCP server management", command.Description);
    }

    [Fact]
    public void ChannelsCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ChannelsCommand();

        Assert.Equal("channels", command.Name);
        Assert.Equal("Manage channels", command.Description);
    }

    [Fact]
    public void ProviderCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ProviderCommand();

        Assert.Equal("provider", command.Name);
        Assert.Equal("Manage providers", command.Description);
    }
}
