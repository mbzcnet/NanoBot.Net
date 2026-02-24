using System.Reflection;
using System.CommandLine;
using FluentAssertions;
using NanoBot.Cli.Commands;
using NanoBot.Cli;
using Xunit;

namespace NanoBot.Cli.Tests;

public class CommandTests
{
    [Fact]
    public void OnboardCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new OnboardCommand();

        command.Name.Should().Be("onboard");
        command.Description.Should().Contain("Initialize");
        command.Description.Should().Contain("workspace");
    }

    [Fact]
    public void RootCommand_ShouldIncludeOnboard_AndNotIncludeConfigure()
    {
        var getCommands = typeof(Program).GetMethod("GetCommands", BindingFlags.NonPublic | BindingFlags.Static);
        getCommands.Should().NotBeNull();
        var list = (IReadOnlyList<ICliCommand>)getCommands!.Invoke(null, null)!;
        var names = list.Select(c => c.Name).ToList();

        names.Should().Contain("onboard");
        names.Should().NotContain("configure");
    }

    [Fact]
    public void OnboardCommand_CreateCommand_ShouldHaveExpectedOptions()
    {
        var command = new OnboardCommand();
        var cliCommand = command.CreateCommand();

        cliCommand.Name.Should().Be("onboard");
        var options = cliCommand.Options.OfType<Option>().Select(o => o.Name).ToList();
        options.Should().Contain("dir");
        options.Should().Contain("name");
        options.Should().Contain("provider");
        options.Should().Contain("model");
        options.Should().Contain("api-key");
        options.Should().Contain("api-base");
        options.Should().Contain("workspace");
        options.Should().Contain("non-interactive");
    }

    [Fact]
    public void AgentCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new AgentCommand();

        command.Name.Should().Be("agent");
        command.Description.Should().Be("Start Agent interactive mode");
    }

    [Fact]
    public void GatewayCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new GatewayCommand();

        command.Name.Should().Be("gateway");
        command.Description.Should().Be("Start Gateway service mode");
    }

    [Fact]
    public void StatusCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new StatusCommand();

        command.Name.Should().Be("status");
        command.Description.Should().Be("Show Agent status");
    }

    [Fact]
    public void ConfigCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ConfigCommand();

        command.Name.Should().Be("config");
        command.Description.Should().Be("Configuration management");
    }

    [Fact]
    public void SessionCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new SessionCommand();

        command.Name.Should().Be("session");
        command.Description.Should().Be("Session management");
    }

    [Fact]
    public void CronCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new CronCommand();

        command.Name.Should().Be("cron");
        command.Description.Should().Be("Scheduled task management");
    }

    [Fact]
    public void McpCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new McpCommand();

        command.Name.Should().Be("mcp");
        command.Description.Should().Be("MCP server management");
    }

    [Fact]
    public void ChannelsCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ChannelsCommand();

        command.Name.Should().Be("channels");
        command.Description.Should().Be("Manage channels");
    }

    [Fact]
    public void ProviderCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ProviderCommand();

        command.Name.Should().Be("provider");
        command.Description.Should().Be("Manage providers");
    }
}
