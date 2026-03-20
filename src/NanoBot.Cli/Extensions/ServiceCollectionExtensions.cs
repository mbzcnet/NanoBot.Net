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
using NanoBot.Core.Debug;
using NanoBot.Core.Heartbeat;
using NanoBot.Core.Memory;
using NanoBot.Core.Skills;
using NanoBot.Core.Storage;
using NanoBot.Core.Subagents;
using NanoBot.Core.Tools.Browser;
using NanoBot.Core.Tools.Rpa;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Bus;
using NanoBot.Infrastructure.Browser;
using NanoBot.Infrastructure.Cron;
using NanoBot.Infrastructure.Heartbeat;
using NanoBot.Infrastructure.Memory;
using NanoBot.Infrastructure.Resources;
using NanoBot.Infrastructure.Skills;
using NanoBot.Infrastructure.Storage;
using NanoBot.Infrastructure.Subagents;
using NanoBot.Infrastructure.Workspace;
using NanoBot.Infrastructure.Tools.Rpa;
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

        var agentConfig = configuration.GetSection("Agent").Get<AgentConfig>()
            ?? configuration.Get<AgentConfig>()
            ?? new AgentConfig();
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

        // Register DebugState for debugging LLM requests/responses
        services.AddSingleton<IDebugState>(sp =>
        {
            var workspace = sp.GetRequiredService<IWorkspaceManager>();
            return new DebugState(workspace);
        });

        services.AddSingleton<IMessageBus, MessageBus>();
        services.AddSingleton<IBrowserService, BrowserService>();
        services.AddSingleton<IFileStorageService, FileStorageService>();

        // Register RPA services
        services.AddSingleton<ImageOptimizer>();
        services.AddSingleton<IInputSimulator, SharpHookInputSimulator>();
        services.AddSingleton<IScreenCapture>(sp =>
        {
            var logger = sp.GetService<ILogger<ScreenCaptureService>>();
            var platformCapture = ScreenCaptureFactory.Create();
            return new ScreenCaptureService(platformCapture, logger);
        });

        // Register OmniParser client if RPA is enabled in config (check at resolve time)
        services.AddSingleton<IOmniParserClient>(sp =>
        {
            var agentConfig = sp.GetService<AgentConfig>();
            if (agentConfig?.Rpa?.Enabled == true && !string.IsNullOrEmpty(agentConfig.Rpa.InstallPath))
            {
                var logger = sp.GetService<ILogger<OmniParserServiceManager>>();
                return new OmniParserServiceManager(
                    agentConfig.Rpa.InstallPath,
                    agentConfig.Rpa.ServicePort,
                    logger);
            }
            // Return a no-op client when not configured
            return new OmniParserClient("127.0.0.1", 18999, null);
        });

        services.AddSingleton<IRpaService>(sp =>
        {
            var inputSimulator = sp.GetRequiredService<IInputSimulator>();
            var screenCapture = sp.GetRequiredService<IScreenCapture>();
            var omniParserClient = sp.GetService<IOmniParserClient>();
            var imageOptimizer = sp.GetRequiredService<ImageOptimizer>();
            var logger = sp.GetService<ILogger<RpaService>>();
            var agentConfig = sp.GetService<AgentConfig>();
            var rpaConfig = agentConfig?.Rpa ?? new RpaToolsConfig();
            return new RpaService(
                inputSimulator,
                screenCapture,
                omniParserClient,
                imageOptimizer,
                logger,
                rpaConfig);
        });

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
            var chatClient = sp.GetService<IChatClient>();
            var logger = sp.GetRequiredService<ILogger<HeartbeatService>>();
            var cfg = heartbeatConfig ?? sp.GetService<HeartbeatConfig>() ?? new HeartbeatConfig();
            return new HeartbeatService(
                workspaceManager,
                chatClient,
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
            var subagentManager = sp.GetService<ISubagentManager>();
            var agentConfig = sp.GetService<AgentConfig>();
            var memoryWindow = agentConfig?.Memory?.MemoryWindow ?? 50;
            var logger = sp.GetService<ILogger<AgentRuntime>>();
            var debugState = sp.GetService<IDebugState>();

            // Get profile-aware dependencies for WebUI support
            var chatClientFactory = sp.GetService<IChatClientFactory>();
            var llmConfig = sp.GetService<LlmConfig>();

            return new AgentRuntime(
                agent,
                bus,
                sessionManager,
                workspace,
                memoryStore,
                subagentManager,
                memoryWindow,
                chatClientFactory,
                llmConfig,
                sp,
                logger,
                debugState);
        });

        return services;
    }

    public static IServiceCollection AddNanoBot(
        this IServiceCollection services,
        IConfiguration configuration,
        AgentOptions? agentOptions = null)
    {
        var agentConfig = configuration.GetSection("Agent").Get<AgentConfig>()
            ?? configuration.Get<AgentConfig>();

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
                    ?? configuration.GetSection("heartbeat").Get<HeartbeatConfig>(),
                Rpa = configuration.GetSection("Rpa").Get<RpaToolsConfig>()
                    ?? configuration.GetSection("rpa").Get<RpaToolsConfig>()
            };
        }

        return services.AddNanoBot(agentConfig, agentOptions);
    }

    public static IServiceCollection AddNanoBot(
        this IServiceCollection services,
        AgentConfig agentConfig,
        AgentOptions? agentOptions = null)
    {
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

        services
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

        // Read DefaultProfile
        llm.DefaultProfile = configuration["llm:default_profile"] ?? configuration["llm:DefaultProfile"];

        // Read Profiles section
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
            {
                profile.Temperature = temp;
            }

            if (int.TryParse(profileSection["max_tokens"] ?? profileSection["maxTokens"] ?? profileSection["MaxTokens"], out var maxTokens))
            {
                profile.MaxTokens = maxTokens;
            }

            llm.Profiles[profileName] = profile;
        }

        // If no profiles were loaded, fallback to flat keys (legacy format)
        if (llm.Profiles.Count == 0)
        {
            var defaultProfile = new LlmProfile
            {
                Name = "default"
            };

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
            llm.DefaultProfile ??= "default";
        }

        return llm;
    }

    public static IServiceCollection AddNanoBotCli(
        this IServiceCollection services,
        string? configPath = null,
        AgentOptions? agentOptions = null)
    {
        var configuration = BuildConfiguration(configPath);
        services.AddSingleton(configuration); // Register configuration
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
