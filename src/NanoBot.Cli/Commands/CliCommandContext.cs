using Microsoft.Extensions.DependencyInjection;
using NanoBot.Core.Configuration;

namespace NanoBot.Cli.Commands;

public sealed class CliCommandContext
{
    public AgentConfig Config { get; }
    public string ConfigPath { get; }
    public IServiceProvider ServiceProvider { get; }

    public CliCommandContext(AgentConfig config, string configPath, IServiceProvider serviceProvider)
    {
        Config = config;
        ConfigPath = configPath;
        ServiceProvider = serviceProvider;
    }

    public T GetService<T>() where T : notnull =>
        ServiceProvider.GetRequiredService<T>();

    public string GetResolvedConfigPath()
    {
        if (!string.IsNullOrEmpty(ConfigPath))
            return Path.GetFullPath(ConfigPath);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nbot", "config.json");
    }
}
