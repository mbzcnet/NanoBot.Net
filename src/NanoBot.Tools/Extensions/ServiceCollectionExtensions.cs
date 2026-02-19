using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NanoBot.Tools.Mcp;

namespace NanoBot.Tools.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTools(this IServiceCollection services)
    {
        services.AddSingleton<IMcpClient, NanoBotMcpClient>();
        services.AddHttpClient("Tools");

        return services;
    }

    public static IServiceCollection AddDefaultTools(
        this IServiceCollection services,
        string? allowedDir = null)
    {
        services.AddSingleton<IReadOnlyList<AITool>>(sp =>
            ToolProvider.CreateDefaultTools(sp, allowedDir));

        return services;
    }
}
