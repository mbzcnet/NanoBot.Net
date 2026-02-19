using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Tools.Mcp;

namespace NanoBot.Cli.Commands;

public class McpCommand : ICliCommand
{
    public string Name => "mcp";
    public string Description => "MCP server management";

    public Command CreateCommand()
    {
        var listCommand = new Command("list", "List MCP servers");
        listCommand.SetHandler(async (context) =>
        {
            var cancellationToken = context.GetCancellationToken();
            await ListServersAsync(cancellationToken);
        });

        var toolsCommand = new Command("tools", "List available tools");
        var serverOption = new Option<string?>(
            name: "--server",
            description: "Server name to list tools from"
        );
        serverOption.AddAlias("-s");
        toolsCommand.Add(serverOption);
        toolsCommand.SetHandler(async (context) =>
        {
            var server = context.ParseResult.GetValueForOption(serverOption);
            var cancellationToken = context.GetCancellationToken();
            await ListToolsAsync(server, cancellationToken);
        });

        var connectCommand = new Command("connect", "Connect to MCP server");
        var connectNameArg = new Argument<string>("name", "Server name");
        var connectCommandArg = new Argument<string>("command", "Command to run");
        var argsOption = new Option<string[]?>(
            name: "--args",
            description: "Command arguments"
        );
        connectCommand.Add(connectNameArg);
        connectCommand.Add(connectCommandArg);
        connectCommand.Add(argsOption);
        connectCommand.SetHandler(async (context) =>
        {
            var name = context.ParseResult.GetValueForArgument(connectNameArg);
            var cmd = context.ParseResult.GetValueForArgument(connectCommandArg);
            var args = context.ParseResult.GetValueForOption(argsOption);
            var cancellationToken = context.GetCancellationToken();
            await ConnectServerAsync(name, cmd, args, cancellationToken);
        });

        var disconnectCommand = new Command("disconnect", "Disconnect MCP server");
        var disconnectNameArg = new Argument<string>("name", "Server name");
        disconnectCommand.Add(disconnectNameArg);
        disconnectCommand.SetHandler(async (context) =>
        {
            var name = context.ParseResult.GetValueForArgument(disconnectNameArg);
            var cancellationToken = context.GetCancellationToken();
            await DisconnectServerAsync(name, cancellationToken);
        });

        var command = new Command(Name, Description);
        command.AddCommand(listCommand);
        command.AddCommand(toolsCommand);
        command.AddCommand(connectCommand);
        command.AddCommand(disconnectCommand);

        return command;
    }

    private static async Task ListServersAsync(CancellationToken cancellationToken)
    {
        var config = await ConfigurationLoader.LoadWithDefaultsAsync(null, cancellationToken);

        Console.WriteLine("MCP Servers:\n");

        if (config.Mcp?.Servers == null || config.Mcp.Servers.Count == 0)
        {
            Console.WriteLine("No MCP servers configured.");
            Console.WriteLine("\nTo add an MCP server, edit ~/.nbot/config.json:");
            Console.WriteLine(@"
{
  ""mcp"": {
    ""servers"": {
      ""my-server"": {
        ""command"": ""npx"",
        ""args"": [""-y"", ""@modelcontextprotocol/server-filesystem"", ""/path/to/dir""]
      }
    }
  }
}
");
            return;
        }

        Console.WriteLine($"{"Name",-20} {"Command",-30} {"Status"}");
        Console.WriteLine(new string('-', 70));

        foreach (var (name, server) in config.Mcp.Servers)
        {
            var cmd = server.Command ?? "unknown";
            var status = "configured";
            Console.WriteLine($"{name,-20} {cmd,-30} {status}");
        }

        Console.WriteLine($"\nTotal: {config.Mcp.Servers.Count} server(s)");
    }

    private static async Task ListToolsAsync(string? serverName, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IMcpClient, NanoBotMcpClient>();
        var serviceProvider = services.BuildServiceProvider();

        var mcpClient = serviceProvider.GetRequiredService<IMcpClient>();

        var config = await ConfigurationLoader.LoadWithDefaultsAsync(null, cancellationToken);

        if (config.Mcp?.Servers == null || config.Mcp.Servers.Count == 0)
        {
            Console.WriteLine("No MCP servers configured.");
            return;
        }

        Console.WriteLine("Available MCP Tools:\n");

        foreach (var (name, serverConfig) in config.Mcp.Servers)
        {
            if (!string.IsNullOrEmpty(serverName) && !name.Equals(serverName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Console.WriteLine($"Server: {name}");

            try
            {
                var mcpConfig = new Tools.Mcp.McpServerConfig
                {
                    Command = serverConfig.Command ?? "",
                    Args = serverConfig.Args ?? Array.Empty<string>(),
                    Env = serverConfig.Env ?? new Dictionary<string, string>(),
                    Cwd = serverConfig.Cwd
                };

                await mcpClient.ConnectAsync(name, mcpConfig, cancellationToken);
                var tools = await mcpClient.ListToolsAsync(name, cancellationToken);

                if (tools.Count == 0)
                {
                    Console.WriteLine("  No tools available");
                }
                else
                {
                    foreach (var tool in tools)
                    {
                        Console.WriteLine($"  • {tool.Name}");
                        if (!string.IsNullOrEmpty(tool.Description))
                        {
                            Console.WriteLine($"    {tool.Description}");
                        }
                    }
                }

                await mcpClient.DisconnectAsync(name, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
            }

            Console.WriteLine();
        }
    }

    private static async Task ConnectServerAsync(
        string name,
        string command,
        string[]? args,
        CancellationToken cancellationToken)
    {
        var config = await ConfigurationLoader.LoadWithDefaultsAsync(null, cancellationToken);

        config.Mcp ??= new McpConfig();
        config.Mcp.Servers ??= new Dictionary<string, Core.Configuration.McpServerConfig>();

        config.Mcp.Servers[name] = new Core.Configuration.McpServerConfig
        {
            Command = command,
            Args = args?.ToList() ?? new List<string>(),
            Env = new Dictionary<string, string>()
        };

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configPath = Path.Combine(homeDir, ".nbot", "config.json");
        await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);

        Console.WriteLine($"✓ MCP server '{name}' added to configuration");
        Console.WriteLine($"  Command: {command}");
        if (args != null && args.Length > 0)
        {
            Console.WriteLine($"  Args: {string.Join(" ", args)}");
        }
    }

    private static async Task DisconnectServerAsync(string name, CancellationToken cancellationToken)
    {
        var config = await ConfigurationLoader.LoadWithDefaultsAsync(null, cancellationToken);

        if (config.Mcp?.Servers == null || !config.Mcp.Servers.ContainsKey(name))
        {
            Console.WriteLine($"MCP server '{name}' not found");
            return;
        }

        config.Mcp.Servers.Remove(name);

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configPath = Path.Combine(homeDir, ".nbot", "config.json");
        await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);

        Console.WriteLine($"✓ MCP server '{name}' removed from configuration");
    }
}
