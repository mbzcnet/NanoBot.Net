using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;
using NanoBot.Core.Cron;
using NanoBot.Core.Heartbeat;
using NanoBot.Core.Skills;
using NanoBot.Core.Subagents;
using NanoBot.Core.Tools.Browser;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Bus;
using NanoBot.Infrastructure.Browser;
using NanoBot.Infrastructure.Cron;
using NanoBot.Infrastructure.Heartbeat;
using NanoBot.Infrastructure.Resources;
using NanoBot.Infrastructure.Skills;
using NanoBot.Infrastructure.Subagents;
using NanoBot.Infrastructure.Workspace;

namespace NanoBot.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkspaceServices(
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

        services.AddSingleton<IBootstrapLoader, BootstrapLoader>();

        return services;
    }

    public static IServiceCollection AddMessageBusServices(this IServiceCollection services)
    {
        services.AddSingleton<IMessageBus, MessageBus>();
        return services;
    }

    public static IServiceCollection AddCronServices(
        this IServiceCollection services,
        string? storePath = null)
    {
        services.AddSingleton<ICronService>(sp =>
        {
            var workspaceManager = sp.GetRequiredService<IWorkspaceManager>();
            var logger = sp.GetRequiredService<ILogger<CronService>>();
            var path = storePath ?? Path.Combine(workspaceManager.GetWorkspacePath(), ".cron", "store.json");
            return new CronService(path, logger);
        });

        return services;
    }

    public static IServiceCollection AddCronServices(
        this IServiceCollection services,
        string storePath,
        Func<CronJob, Task<string?>> onJobCallback)
    {
        services.AddSingleton<ICronService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<CronService>>();
            return new CronService(storePath, logger, onJobCallback);
        });

        return services;
    }

    public static IServiceCollection AddHeartbeatServices(
        this IServiceCollection services,
        HeartbeatConfig? config = null,
        Func<string, Task<string>>? onHeartbeat = null)
    {
        services.AddSingleton<IHeartbeatService>(sp =>
        {
            var workspaceManager = sp.GetRequiredService<IWorkspaceManager>();
            var chatClient = sp.GetService<IChatClient>();
            var logger = sp.GetRequiredService<ILogger<HeartbeatService>>();
            var cfg = config ?? sp.GetService<HeartbeatConfig>() ?? new HeartbeatConfig();
            return new HeartbeatService(
                workspaceManager,
                chatClient,
                logger,
                onHeartbeat,
                cfg.IntervalSeconds,
                cfg.Enabled);
        });

        return services;
    }

    public static IServiceCollection AddSkillsServices(this IServiceCollection services)
    {
        services.AddSingleton<ISkillsLoader, SkillsLoader>();
        return services;
    }

    public static IServiceCollection AddSubagentServices(
        this IServiceCollection services,
        Func<string, string, Task<string>>? executeSubagent = null)
    {
        services.AddSingleton<ISubagentManager>(sp =>
        {
            var messageBus = sp.GetRequiredService<IMessageBus>();
            var workspaceManager = sp.GetRequiredService<IWorkspaceManager>();
            var logger = sp.GetRequiredService<ILogger<SubagentManager>>();
            return new SubagentManager(messageBus, workspaceManager, logger, executeSubagent);
        });

        return services;
    }

    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        WorkspaceConfig? workspaceConfig = null,
        HeartbeatConfig? heartbeatConfig = null)
    {
        services.AddWorkspaceServices(workspaceConfig);
        services.AddMessageBusServices();
        services.AddCronServices();
        services.AddHeartbeatServices(heartbeatConfig);
        services.AddSkillsServices();
        services.AddSubagentServices();
        services.AddSingleton<IPlaywrightSessionManager, PlaywrightSessionManager>();
        services.AddSingleton<IBrowserService, BrowserService>();

        return services;
    }
}
