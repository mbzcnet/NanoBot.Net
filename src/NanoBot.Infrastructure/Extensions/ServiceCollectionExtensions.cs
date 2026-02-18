using Microsoft.Extensions.DependencyInjection;
using NanoBot.Core.Configuration;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Resources;
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
}
