using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Agent.Context;
using NanoBot.Core.Bus;
using NanoBot.Core.Skills;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNanoBotAgent(
        this IServiceCollection services,
        IReadOnlyList<AITool>? tools = null)
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
            var loggerFactory = sp.GetService<ILoggerFactory>();

            var resolvedTools = tools ?? sp.GetServices<AITool>().ToList();

            return NanoBotAgentFactory.Create(
                chatClient,
                workspace,
                skillsLoader,
                resolvedTools,
                loggerFactory);
        });

        services.AddSingleton<IAgentRuntime>(sp =>
        {
            var agent = sp.GetRequiredService<ChatClientAgent>();
            var bus = sp.GetRequiredService<IMessageBus>();
            var sessionManager = sp.GetRequiredService<ISessionManager>();
            var workspace = sp.GetRequiredService<IWorkspaceManager>();
            var logger = sp.GetService<ILogger<AgentRuntime>>();

            return new AgentRuntime(
                agent,
                bus,
                sessionManager,
                workspace.GetSessionsPath(),
                logger);
        });

        return services;
    }
}
