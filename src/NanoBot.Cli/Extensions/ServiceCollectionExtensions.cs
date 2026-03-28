using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NanoBot.Core.Configuration;

namespace NanoBot.Cli.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNanoBotCli(
        this IServiceCollection services,
        string? configPath = null,
        NanoBot.Agent.AgentOptions? agentOptions = null)
    {
        var configuration = BuildConfiguration(configPath);
        services.AddSingleton(configuration);
        return services.AddNanoBot(configuration, agentOptions);
    }

    public static IServiceCollection AddNanoBot(
        this IServiceCollection services,
        AgentConfig agentConfig,
        NanoBot.Agent.AgentOptions? agentOptions = null)
    {
        return NanoBot.Agent.ServiceCollectionExtensions.AddNanoBot(services, agentConfig, agentOptions);
    }

    public static IServiceCollection AddNanoBot(
        this IServiceCollection services,
        IConfiguration configuration,
        NanoBot.Agent.AgentOptions? agentOptions = null)
    {
        var agentConfig = configuration.GetSection("Agent").Get<AgentConfig>()
            ?? configuration.Get<AgentConfig>();

        if (agentConfig == null)
        {
            agentConfig = new AgentConfig
            {
                Name = configuration["Name"] ?? configuration["name"] ?? "NanoBot",
                Workspace = configuration.GetSection("Workspace").Get<WorkspaceConfig>()
                    ?? configuration.GetSection("workspace").Get<WorkspaceConfig>()
                    ?? new WorkspaceConfig(),
                Llm = BindLlmConfig(configuration),
                Channels = configuration.GetSection("Channels").Get<ChannelsConfig>()
                    ?? configuration.GetSection("channels").Get<ChannelsConfig>()
                    ?? new ChannelsConfig(),
                Mcp = configuration.GetSection("Mcp").Get<McpConfig>()
                    ?? configuration.GetSection("mcp").Get<McpConfig>(),
                Security = configuration.GetSection("Security").Get<SecurityConfig>()
                    ?? configuration.GetSection("security").Get<SecurityConfig>()
                    ?? new SecurityConfig(),
                Memory = configuration.GetSection("Memory").Get<MemoryConfig>()
                    ?? configuration.GetSection("memory").Get<MemoryConfig>()
                    ?? new MemoryConfig(),
                Heartbeat = configuration.GetSection("Heartbeat").Get<HeartbeatConfig>()
                    ?? configuration.GetSection("heartbeat").Get<HeartbeatConfig>(),
                Rpa = configuration.GetSection("Rpa").Get<RpaToolsConfig>()
                    ?? configuration.GetSection("rpa").Get<RpaToolsConfig>()
            };
        }

        return NanoBot.Agent.ServiceCollectionExtensions.AddNanoBot(services, agentConfig, agentOptions);
    }

    private static LlmConfig BindLlmConfig(IConfiguration configuration)
    {
        var llm = new LlmConfig();

        llm.DefaultProfile = configuration["llm:default_profile"] ?? configuration["llm:DefaultProfile"];

        var profilesSection = configuration.GetSection("llm:profiles");
        foreach (var profileSection in profilesSection.GetChildren())
        {
            var profileName = profileSection.Key;
            var profile = new LlmProfile
            {
                Name = profileSection["name"] ?? profileSection["Name"] ?? profileName,
                Model = profileSection["model"] ?? profileSection["Model"] ?? string.Empty,
                ApiKey = profileSection["api_key"] ?? profileSection["apiKey"] ?? profileSection["ApiKey"],
                ApiBase = profileSection["api_base"] ?? profileSection["apiBase"] ?? profileSection["ApiBase"],
                Provider = profileSection["provider"] ?? profileSection["Provider"],
                SystemPrompt = profileSection["system_prompt"] ?? profileSection["systemPrompt"] ?? profileSection["SystemPrompt"]
            };

            if (double.TryParse(profileSection["temperature"] ?? profileSection["Temperature"], out var temp))
                profile.Temperature = temp;

            if (int.TryParse(profileSection["max_tokens"] ?? profileSection["maxTokens"] ?? profileSection["MaxTokens"], out var maxTokens))
                profile.MaxTokens = maxTokens;

            llm.Profiles[profileName] = profile;
        }

        if (llm.Profiles.Count == 0)
        {
            var defaultProfile = new LlmProfile { Name = "default" };

            var model = configuration["llm:model"] ?? configuration["llm:Model"];
            if (!string.IsNullOrEmpty(model)) defaultProfile.Model = model;

            var apiKey = configuration["llm:api_key"] ?? configuration["llm:ApiKey"] ?? configuration["llm:apiKey"];
            if (!string.IsNullOrEmpty(apiKey)) defaultProfile.ApiKey = apiKey;

            var apiBase = configuration["llm:api_base"] ?? configuration["llm:ApiBase"] ?? configuration["llm:apiBase"];
            if (!string.IsNullOrEmpty(apiBase)) defaultProfile.ApiBase = apiBase;

            var provider = configuration["llm:provider"] ?? configuration["llm:Provider"];
            if (!string.IsNullOrEmpty(provider)) defaultProfile.Provider = provider;

            if (double.TryParse(configuration["llm:temperature"] ?? configuration["llm:Temperature"], out var temperature))
                defaultProfile.Temperature = temperature;

            if (int.TryParse(configuration["llm:max_tokens"] ?? configuration["llm:MaxTokens"], out var maxTokens))
                defaultProfile.MaxTokens = maxTokens;

            var systemPrompt = configuration["llm:system_prompt"] ?? configuration["llm:SystemPrompt"];
            if (!string.IsNullOrEmpty(systemPrompt)) defaultProfile.SystemPrompt = systemPrompt;

            llm.Profiles["default"] = defaultProfile;
            llm.DefaultProfile ??= "default";
        }

        return llm;
    }

    private static IConfiguration BuildConfiguration(string? configPath = null)
    {
        var builder = new ConfigurationBuilder();

        if (configPath != null && File.Exists(configPath))
        {
            builder.AddJsonFile(configPath, optional: false, reloadOnChange: false);
        }
        else
        {
            var defaultPaths = new[]
            {
                "config.json",
                "agent.json",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nbot", "config.json")
            };

            foreach (var path in defaultPaths)
            {
                if (File.Exists(path))
                {
                    builder.AddJsonFile(path, optional: false, reloadOnChange: false);
                    break;
                }
            }
        }

        builder.AddEnvironmentVariables("NBOT_");

        return builder.Build();
    }
}
