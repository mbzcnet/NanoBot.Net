using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NanoBot.Core.Bus;
using NanoBot.Core.Cron;
using NanoBot.Core.Subagents;
using NanoBot.Tools.Mcp;

namespace NanoBot.Tools;

public static class ToolProvider
{
    public static async Task<IReadOnlyList<AITool>> CreateDefaultToolsAsync(
        IServiceProvider services,
        string? allowedDir = null,
        string? defaultChannel = null,
        string? defaultChatId = null,
        CancellationToken cancellationToken = default)
    {
        var tools = new List<AITool>();

        var messageBus = services.GetService<IMessageBus>();
        var cronService = services.GetService<ICronService>();
        var subagentManager = services.GetService<ISubagentManager>();
        var httpClientFactory = services.GetService<IHttpClientFactory>();
        var mcpClient = services.GetService<IMcpClient>();
        var httpClient = httpClientFactory?.CreateClient("Tools");

        tools.Add(BuiltIn.FileTools.CreateReadFileTool(allowedDir));
        tools.Add(BuiltIn.FileTools.CreateWriteFileTool(allowedDir));
        tools.Add(BuiltIn.FileTools.CreateEditFileTool(allowedDir));
        tools.Add(BuiltIn.FileTools.CreateListDirTool(allowedDir));

        tools.Add(BuiltIn.ShellTools.CreateExecTool());

        tools.Add(BuiltIn.WebTools.CreateWebSearchTool(httpClient));
        tools.Add(BuiltIn.WebTools.CreateWebFetchTool(httpClient));

        tools.Add(BuiltIn.MessageTools.CreateMessageTool(messageBus, defaultChannel, defaultChatId));
        tools.Add(BuiltIn.CronTools.CreateCronTool(cronService, defaultChannel, defaultChatId));
        tools.Add(BuiltIn.SpawnTools.CreateSpawnTool(subagentManager, defaultChannel, defaultChatId));

        if (mcpClient != null && mcpClient.ConnectedServers.Count > 0)
        {
            var mcpTools = await mcpClient.GetAllAIToolsAsync(cancellationToken);
            tools.AddRange(mcpTools);
        }

        return tools;
    }

    public static IReadOnlyList<AITool> CreateDefaultTools(
        IServiceProvider services,
        string? allowedDir = null,
        string? defaultChannel = null,
        string? defaultChatId = null)
    {
        return CreateDefaultToolsAsync(services, allowedDir, defaultChannel, defaultChatId).GetAwaiter().GetResult();
    }
}
