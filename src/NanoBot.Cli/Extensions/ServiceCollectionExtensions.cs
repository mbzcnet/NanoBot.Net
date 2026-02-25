using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Agent;
using NanoBot.Channels;
using NanoBot.Core.Bus;
using NanoBot.Core.Channels;
using NanoBot.Core.Configuration;
using NanoBot.Core.Cron;
using NanoBot.Core.Heartbeat;
using NanoBot.Core.Memory;
using NanoBot.Core.Skills;
using NanoBot.Core.Subagents;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Bus;
using NanoBot.Infrastructure.Cron;
using NanoBot.Infrastructure.Heartbeat;
using NanoBot.Infrastructure.Memory;
using NanoBot.Infrastructure.Resources;
using NanoBot.Infrastructure.Skills;
using NanoBot.Infrastructure.Subagents;
using NanoBot.Infrastructure.Workspace;
using NanoBot.Providers;
using NanoBot.Tools.Extensions;

namespace NanoBot.Cli.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNanoBotConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(configuration);

        var agentConfig = configuration.GetSection("Agent").Get<AgentConfig>() ?? new AgentConfig();
        services.AddSingleton(agentConfig);

        services.AddSingleton(agentConfig.Workspace);
        services.AddSingleton(agentConfig.Llm);
        services.AddSingleton(agentConfig.Channels);

        if (agentConfig.Mcp != null)
        {
            services.AddSingleton(agentConfig.Mcp);
        }

        if (agentConfig.Heartbeat != null)
        {
            services.AddSingleton(agentConfig.Heartbeat);
        }

        services.AddSingleton(agentConfig.Security);
        services.AddSingleton(agentConfig.Memory);

        return services;
    }

    public static IServiceCollection AddMicrosoftAgentsAI(
        this IServiceCollection services,
        LlmConfig config)
    {
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();

        services.AddSingleton<IChatClient>(sp =>
        {
            var factory = sp.GetRequiredService<IChatClientFactory>();
            return factory.CreateChatClient(config);
        });

        return services;
    }

    public static IServiceCollection AddNanoBotTools(
        this IServiceCollection services,
        string? allowedDir = null)
    {
        services.AddTools();
        services.AddDefaultTools(allowedDir);

        return services;
    }

    public static IServiceCollection AddNanoBotContextProviders(
        this IServiceCollection services)
    {
        services.AddSingleton<IMemoryStore>(sp =>
        {
            var workspace = sp.GetRequiredService<IWorkspaceManager>();
            var config = sp.GetRequiredService<MemoryConfig>();
            var logger = sp.GetService<ILogger<MemoryStore>>();
            return new MemoryStore(workspace, config, logger);
        });

        return services;
    }

    public static IServiceCollection AddNanoBotInfrastructure(
        this IServiceCollection services,
        WorkspaceConfig? workspaceConfig = null)
    {
        services.AddSingleton<IEmbeddedResourceLoader, EmbeddedResourceLoader>();

        services.AddSingleton<IWorkspaceManager>(sp =>
        {
            var config = workspaceConfig ?? sp.GetService<WorkspaceConfig>() ?? new WorkspaceConfig();
            var resourceLoader = sp.GetService<IEmbeddedResourceLoader>();
            return new WorkspaceManager(config, resourceLoader);
        });

        services.AddSingleton<IBootstrapLoader>(sp =>
        {
            var workspace = sp.GetRequiredService<IWorkspaceManager>();
            return new BootstrapLoader(workspace);
        });

        services.AddSingleton<IMessageBus, MessageBus>();

        return services;
    }

    public static IServiceCollection AddNanoBotBackgroundServices(
        this IServiceCollection services,
        HeartbeatConfig? heartbeatConfig = null,
        Func<string, Task<string>>? onHeartbeat = null)
    {
        services.AddSingleton<ICronService>(sp =>
        {
            var workspaceManager = sp.GetRequiredService<IWorkspaceManager>();
            var logger = sp.GetRequiredService<ILogger<CronService>>();
            var storePath = Path.Combine(workspaceManager.GetWorkspacePath(), ".cron", "store.json");
            return new CronService(storePath, logger);
        });

        services.AddSingleton<IHeartbeatService>(sp =>
        {
            var workspaceManager = sp.GetRequiredService<IWorkspaceManager>();
            var logger = sp.GetRequiredService<ILogger<HeartbeatService>>();
            var cfg = heartbeatConfig ?? sp.GetService<HeartbeatConfig>() ?? new HeartbeatConfig();
            return new HeartbeatService(
                workspaceManager,
                logger,
                onHeartbeat,
                cfg.IntervalSeconds,
                cfg.Enabled);
        });

        services.AddSingleton<ISkillsLoader>(sp =>
        {
            var workspace = sp.GetRequiredService<IWorkspaceManager>();
            var resourceLoader = sp.GetRequiredService<IEmbeddedResourceLoader>();
            var logger = sp.GetRequiredService<ILogger<SkillsLoader>>();
            return new SkillsLoader(workspace, resourceLoader, logger);
        });

        services.AddSingleton<ISubagentManager>(sp =>
        {
            var messageBus = sp.GetRequiredService<IMessageBus>();
            var workspaceManager = sp.GetRequiredService<IWorkspaceManager>();
            var logger = sp.GetRequiredService<ILogger<SubagentManager>>();
            return new SubagentManager(messageBus, workspaceManager, logger);
        });

        return services;
    }

    public static IServiceCollection AddNanoBotChannels(
        this IServiceCollection services,
        ChannelsConfig? channelsConfig = null)
    {
        services.AddSingleton<IChannelManager>(sp =>
        {
            var bus = sp.GetRequiredService<IMessageBus>();
            var logger = sp.GetRequiredService<ILogger<ChannelManager>>();
            return new ChannelManager(bus, logger);
        });

        return services;
    }

    public static IServiceCollection AddNanoBotAgent(
        this IServiceCollection services,
        AgentOptions? options = null)
    {
        services.AddSingleton<ISessionManager>(sp =>
        {
            var agent = sp.GetRequiredService<ChatClientAgent>();
            var workspace = sp.GetRequiredService<IWorkspaceManager>();
            var logger = sp.GetService<ILogger<SessionManager>>();
            return new SessionManager(agent, workspace, logger);
        });

        services.AddSingleton<ChatClientAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var workspace = sp.GetRequiredService<IWorkspaceManager>();
            var skillsLoader = sp.GetRequiredService<ISkillsLoader>();
            var memoryStore = sp.GetService<IMemoryStore>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var tools = sp.GetService<IReadOnlyList<AITool>>();
            var agentConfig = sp.GetService<AgentConfig>();
            var memoryWindow = agentConfig?.Memory?.MemoryWindow ?? 50;
            var maxInstructionChars = agentConfig?.Memory?.MaxInstructionChars ?? 0;

            return NanoBotAgentFactory.Create(
                chatClient,
                workspace,
                skillsLoader,
                tools,
                loggerFactory,
                options,
                memoryStore,
                memoryWindow,
                maxInstructionChars);
        });

        services.AddSingleton<IAgentRuntime>(sp =>
        {
            var agent = sp.GetRequiredService<ChatClientAgent>();
            var bus = sp.GetRequiredService<IMessageBus>();
            var sessionManager = sp.GetRequiredService<ISessionManager>();
            var workspace = sp.GetRequiredService<IWorkspaceManager>();
            var memoryStore = sp.GetService<IMemoryStore>();
            var agentConfig = sp.GetService<AgentConfig>();
            var memoryWindow = agentConfig?.Memory?.MemoryWindow ?? 50;
            var logger = sp.GetService<ILogger<AgentRuntime>>();

            return new AgentRuntime(
                agent,
                bus,
                sessionManager,
                workspace,
                memoryStore,
                memoryWindow,
                logger);
        });

        return services;
    }

    public static IServiceCollection AddNanoBot(
        this IServiceCollection services,
        IConfiguration configuration,
        AgentOptions? agentOptions = null)
    {
        var agentConfig = configuration.GetSection("Agent").Get<AgentConfig>();
        if (agentConfig == null)
        {
            var llmSection = configuration.GetSection("llm");
            var llmConfig = llmSection.Exists() 
                ? BindLlmConfig(configuration) 
                : new LlmConfig();

            agentConfig = new AgentConfig
            {
                Name = configuration["Name"] ?? configuration["name"] ?? "NanoBot",
                Workspace = configuration.GetSection("Workspace").Get<WorkspaceConfig>() 
                    ?? configuration.GetSection("workspace").Get<WorkspaceConfig>() 
                    ?? new WorkspaceConfig(),
                Llm = llmConfig,
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
                    ?? configuration.GetSection("heartbeat").Get<HeartbeatConfig>()
            };
        }

        services
            .AddNanoBotConfiguration(configuration)
            .AddMicrosoftAgentsAI(agentConfig.Llm)
            .AddNanoBotInfrastructure(agentConfig.Workspace)
            .AddNanoBotTools(agentConfig.Workspace.GetResolvedPath())
            .AddNanoBotContextProviders()
            .AddNanoBotBackgroundServices(agentConfig.Heartbeat)
            .AddNanoBotChannels(agentConfig.Channels)
            .AddNanoBotAgent(agentOptions);

        return services;
    }

    private static LlmConfig BindLlmConfig(IConfiguration configuration)
    {
        var llm = new LlmConfig();
        
        var defaultProfile = new LlmProfile();

        var model = configuration["llm:model"] ?? configuration["llm:Model"];
        if (!string.IsNullOrEmpty(model))
            defaultProfile.Model = model;

        var apiKey = configuration["llm:api_key"] ?? configuration["llm:ApiKey"] ?? configuration["llm:apiKey"];
        if (!string.IsNullOrEmpty(apiKey))
            defaultProfile.ApiKey = apiKey;

        var apiBase = configuration["llm:api_base"] ?? configuration["llm:ApiBase"] ?? configuration["llm:apiBase"];
        if (!string.IsNullOrEmpty(apiBase))
            defaultProfile.ApiBase = apiBase;

        var provider = configuration["llm:provider"] ?? configuration["llm:Provider"];
        if (!string.IsNullOrEmpty(provider))
            defaultProfile.Provider = provider;

        var temperatureStr = configuration["llm:temperature"] ?? configuration["llm:Temperature"];
        if (double.TryParse(temperatureStr, out var temperature))
            defaultProfile.Temperature = temperature;

        var maxTokensStr = configuration["llm:max_tokens"] ?? configuration["llm:MaxTokens"];
        if (int.TryParse(maxTokensStr, out var maxTokens))
            defaultProfile.MaxTokens = maxTokens;

        var systemPrompt = configuration["llm:system_prompt"] ?? configuration["llm:SystemPrompt"];
        if (!string.IsNullOrEmpty(systemPrompt))
            defaultProfile.SystemPrompt = systemPrompt;

        llm.Profiles["default"] = defaultProfile;
        return llm;
    }

    public static IServiceCollection AddNanoBotCli(
        this IServiceCollection services,
        string? configPath = null,
        AgentOptions? agentOptions = null)
    {
        var configuration = BuildConfiguration(configPath);
        return services.AddNanoBot(configuration, agentOptions);
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
