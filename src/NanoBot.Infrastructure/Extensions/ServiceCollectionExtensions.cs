using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;
using NanoBot.Core.Cron;
using NanoBot.Core.Heartbeat;
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
using NanoBot.Infrastructure.Resources;
using NanoBot.Infrastructure.Skills;
using NanoBot.Infrastructure.Storage;
using NanoBot.Infrastructure.Subagents;
using NanoBot.Infrastructure.Workspace;
using NanoBot.Infrastructure.Tools.Rpa;

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

    public static IServiceCollection AddBrowserServices(this IServiceCollection services)
    {
        services.AddSingleton<IPowerShellInstaller, PowerShellInstaller>();
        services.AddSingleton<IPlaywrightInstaller, PlaywrightInstaller>();
        services.AddSingleton<IBrowserService, BrowserService>();
        return services;
    }

    public static IServiceCollection AddRpaServices(
        this IServiceCollection services,
        RpaToolsConfig? config = null)
    {
        config ??= new RpaToolsConfig();

        // 添加 ImageOptimizer
        services.AddSingleton<ImageOptimizer>();

        // 添加平台特定的截图服务
        services.AddSingleton<IScreenCapture>(sp =>
        {
            var logger = sp.GetService<ILogger<ScreenCaptureService>>();
            var platformCapture = ScreenCaptureFactory.Create();
            return new ScreenCaptureService(platformCapture, logger);
        });

        // 添加 SharpHook 输入模拟器
        services.AddSingleton<IInputSimulator, SharpHookInputSimulator>();

        // 条件添加 OmniParser Client
        if (config.Enabled && !string.IsNullOrEmpty(config.InstallPath))
        {
            services.AddSingleton<IOmniParserClient>(sp =>
            {
                var logger = sp.GetService<ILogger<OmniParserServiceManager>>();
                return new OmniParserServiceManager(
                    config.InstallPath,
                    config.ServicePort,
                    logger);
            });
        }
        else
        {
            // 添加一个 no-op OmniParser Client，当未配置时使用
            services.AddSingleton<IOmniParserClient, OmniParserClient>();
        }

        // 添加 RPA 服务
        services.AddSingleton<IRpaService>(sp =>
        {
            var inputSimulator = sp.GetRequiredService<IInputSimulator>();
            var screenCapture = sp.GetRequiredService<IScreenCapture>();
            var omniParserClient = sp.GetService<IOmniParserClient>();
            var imageOptimizer = sp.GetRequiredService<ImageOptimizer>();
            var logger = sp.GetService<ILogger<RpaService>>();
            return new RpaService(
                inputSimulator,
                screenCapture,
                omniParserClient,
                imageOptimizer,
                logger,
                config);
        });

        return services;
    }

    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        WorkspaceConfig? workspaceConfig = null,
        HeartbeatConfig? heartbeatConfig = null,
        RpaToolsConfig? rpaConfig = null)
    {
        services.AddWorkspaceServices(workspaceConfig);
        services.AddMessageBusServices();
        services.AddCronServices();
        services.AddHeartbeatServices(heartbeatConfig);
        services.AddSkillsServices();
        services.AddSubagentServices();
        services.AddBrowserServices();
        services.AddRpaServices(rpaConfig);
        services.AddSingleton<IFileStorageService, FileStorageService>();

        return services;
    }
}
