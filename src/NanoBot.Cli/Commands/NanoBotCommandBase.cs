using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using NanoBot.Core.Configuration;

namespace NanoBot.Cli.Commands;

public abstract class NanoBotCommandBase : ICliCommand
{
    protected CliCommandContext Context { get; }

    protected NanoBotCommandBase(CliCommandContext context)
    {
        Context = context;
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract Command CreateCommand();

    protected T GetService<T>() where T : notnull => Context.GetService<T>();

    protected AgentConfig GetConfig() => Context.Config;

    protected string GetConfigPath() => Context.GetResolvedConfigPath();
}
