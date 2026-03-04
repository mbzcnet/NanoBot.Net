using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using NanoBot.Core.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NanoBot.Cli.Commands;

public abstract class NanoBotCommandBase : ICliCommand
{
    protected static IServiceProvider? SharedServiceProvider { get; private set; }
    protected static AgentConfig? SharedConfig { get; private set; }
    protected static string? SharedConfigPath { get; private set; }

    public static void Initialize(IServiceProvider provider, AgentConfig config, string? configPath = null)
    {
        SharedServiceProvider = provider;
        SharedConfig = config;
        SharedConfigPath = configPath;
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract Command CreateCommand();

    protected T GetService<T>() where T : notnull
    {
        return SharedServiceProvider!.GetRequiredService<T>();
    }

    protected AgentConfig GetConfig()
    {
        return SharedConfig!;
    }

    protected string GetConfigPath()
    {
        if (!string.IsNullOrEmpty(SharedConfigPath))
        {
            return Path.GetFullPath(SharedConfigPath);
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".nbot", "config.json");
    }
}
