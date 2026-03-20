using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;
using NanoBot.Core.Cron;
using NanoBot.Core.Subagents;
using NanoBot.Core.Tools;
using NanoBot.Core.Tools.Browser;
using NanoBot.Core.Tools.Rpa;
using NanoBot.Tools.BuiltIn;
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
        var browserService = services.GetService<IBrowserService>();
        var rpaService = services.GetService<IRpaService>();
        var httpClientFactory = services.GetService<IHttpClientFactory>();
        var mcpClient = services.GetService<IMcpClient>();
        var config = services.GetService<AgentConfig>();
        var httpClient = httpClientFactory?.CreateClient("Tools");

        // Check if enhanced file tools should be used
        var fileToolsConfig = config?.FileTools;
        if (fileToolsConfig?.UseEnhanced == true)
        {
            tools.Add(BuiltIn.FileTools.CreateEnhancedReadFileTool(fileToolsConfig));
            tools.Add(BuiltIn.FileTools.CreateEnhancedEditFileTool(fileToolsConfig));
        }
        else
        {
            tools.Add(BuiltIn.FileTools.CreateReadFileTool(allowedDir));
            tools.Add(BuiltIn.FileTools.CreateEditFileTool(allowedDir));
        }

        tools.Add(BuiltIn.FileTools.CreateWriteFileTool(allowedDir));
        tools.Add(BuiltIn.FileTools.CreateListDirTool(allowedDir));

        tools.Add(BuiltIn.ShellTools.CreateExecTool(new ShellToolOptions()));

        tools.Add(BuiltIn.WebTools.CreateWebSearchTool(httpClient));
        tools.Add(BuiltIn.WebTools.CreateWebFetchTool(httpClient));

        // Only add browser tools if not explicitly disabled in config
        var browserEnabled = config?.Browser?.Enabled != false;
        if (browserEnabled)
        {
            // 使用委托获取当前 sessionKey，避免 AsyncLocal 在异步链中丢失
            tools.Add(BuiltIn.BrowserTools.CreateBrowserTool(browserService, () => ToolExecutionContext.CurrentSessionKey));
        }

        // Only add RPA tools if explicitly enabled in config
        if (config?.Rpa?.Enabled == true && rpaService != null)
        {
            tools.Add(BuiltIn.Rpa.RpaTools.CreateRpaTool(rpaService));
        }

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
