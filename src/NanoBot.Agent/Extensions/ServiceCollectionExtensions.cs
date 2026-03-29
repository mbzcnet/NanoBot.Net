using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Agent.Context;
using NanoBot.Agent.Services;
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

namespace NanoBot.Agent;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Orchestrates the full NanoBot DI registration chain.
    /// </summary>
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
            services.AddSingleton(agentConfig.Mcp);

        if (agentConfig.Heartbeat != null)
            services.AddSingleton(agentConfig.Heartbeat);

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

        services.AddSingleton<IDebugState>(sp =>
        {
            var workspace = sp.GetRequiredService<IWorkspaceManager>();
            return new DebugState(workspace);
        });

        services.AddSingleton<IMessageBus, MessageBus>();
        services.AddSingleton<IBrowserService, BrowserService>();
        services.AddSingleton<IFileStorageService, FileStorageService>();

        services.AddSingleton<ImageOptimizer>();
        services.AddSingleton<IInputSimulator, SharpHookInputSimulator>();
        services.AddSingleton<IScreenCapture>(sp =>
        {
            var logger = sp.GetService<ILogger<ScreenCaptureService>>();
            var platformCapture = ScreenCaptureFactory.Create();
            return new ScreenCaptureService(platformCapture, logger);
        });

        services.AddSingleton<IOmniParserClient>(sp =>
        {
            var agentConfig = sp.GetService<AgentConfig>();
            if (agentConfig?.Rpa?.Enabled == true && !string.IsNullOrEmpty(agentConfig.Rpa.InstallPath))
            {
                var logger = sp.GetService<ILogger<OmniParserServiceManager>>();
                return new OmniParserServiceManager(agentConfig.Rpa.InstallPath, agentConfig.Rpa.ServicePort, logger);
            }
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
            return new RpaService(inputSimulator, screenCapture, omniParserClient, imageOptimizer, logger, rpaConfig);
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
            return new HeartbeatService(workspaceManager, chatClient, logger, onHeartbeat, cfg.IntervalSeconds, cfg.Enabled);
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
        var config = channelsConfig ?? new ChannelsConfig();
        services.AddSingleton(config);

        services.AddSingleton<IChannelManager>(sp =>
        {
            var bus = sp.GetRequiredService<IMessageBus>();
            var logger = sp.GetRequiredService<ILogger<ChannelManager>>();
            var cfg = sp.GetRequiredService<ChannelsConfig>();
            return new ChannelManager(bus, logger, cfg);
        });

        services.AddSingleton<IChannelFactory, ChannelFactory>();
        services.AddHostedService<ChannelStartupService>();

        return services;
    }

    public static IServiceCollection AddNanoBotAgent(
        this IServiceCollection services,
        AgentOptions? options = null)
    {
        services.AddSingleton<ChatClientAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var workspace = sp.GetRequiredService<IWorkspaceManager>();
            var skillsLoader = sp.GetRequiredService<ISkillsLoader>();
            var memoryStore = sp.GetService<IMemoryStore>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var tools = sp.GetServices<AITool>().ToList();
            var agentConfig = sp.GetService<AgentConfig>();
            var memoryWindow = agentConfig?.Memory?.MemoryWindow ?? 50;
            var maxInstructionChars = agentConfig?.Memory?.MaxInstructionChars ?? 0;
            var timezone = agentConfig?.Timezone;

            return NanoBotAgentFactory.Create(
                chatClient,
                workspace,
                skillsLoader,
                tools,
                loggerFactory,
                options,
                memoryStore,
                memoryWindow,
                maxInstructionChars,
                timezone);
        });

        services.AddSingleton<ISessionManager>(sp =>
        {
            var agent = sp.GetRequiredService<ChatClientAgent>();
            var workspace = sp.GetRequiredService<IWorkspaceManager>();
            var logger = sp.GetService<ILogger<SessionManager>>();
            return new SessionManager(agent, workspace, logger);
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
}
