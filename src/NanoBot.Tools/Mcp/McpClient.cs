using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using NanoBot.Core.Configuration;

namespace NanoBot.Tools.Mcp;

public class NanoBotMcpClient : IMcpClient
{
    private readonly ILogger<NanoBotMcpClient>? _logger;
    private readonly ConcurrentDictionary<string, McpClientWrapper> _clients = new();
    private readonly ConcurrentDictionary<string, IList<McpClientTool>> _tools = new();
    private bool _disposed;

    public NanoBotMcpClient(ILogger<NanoBotMcpClient>? logger = null)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> ConnectedServers => _clients.Keys.ToList();

    public async Task ConnectAsync(string serverName, McpServerConfig config, CancellationToken cancellationToken = default)
    {
        if (_clients.ContainsKey(serverName))
        {
            _logger?.LogWarning("MCP server '{ServerName}' is already connected", serverName);
            return;
        }

        try
        {
            _logger?.LogInformation("Connecting to MCP server '{ServerName}'...", serverName);

            // Headers support will be added when stdio transport supports it
            // if (config.Headers != null && config.Headers.Count > 0)
            // {
            //     foreach (var header in config.Headers)
            //     {
            //         _logger?.LogDebug("MCP server '{ServerName}' using custom header: {Key}", serverName, header.Key);
            //     }
            // }

            var transportOptions = new StdioClientTransportOptions
            {
                Name = serverName,
                Command = config.Command,
                Arguments = config.Args.ToArray()
            };

            if (config.Env.Count > 0)
            {
                transportOptions.EnvironmentVariables = config.Env.ToDictionary(k => k.Key, k => (string?)k.Value);
            }

            var transport = new StdioClientTransport(transportOptions);
            var client = await ModelContextProtocol.Client.McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

            _clients[serverName] = new McpClientWrapper(client, transport);

            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            _tools[serverName] = tools;

            _logger?.LogInformation("MCP server '{ServerName}' connected with {ToolCount} tools", serverName, tools.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to MCP server '{ServerName}'", serverName);
            throw;
        }
    }

    public async Task DisconnectAsync(string serverName, CancellationToken cancellationToken = default)
    {
        if (_clients.TryRemove(serverName, out var wrapper))
        {
            _logger?.LogInformation("Disconnecting from MCP server '{ServerName}'...", serverName);

            try
            {
                await wrapper.Client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error disconnecting from MCP server '{ServerName}'", serverName);
            }
            finally
            {
                _tools.TryRemove(serverName, out _);
            }
        }
    }

    public Task<IList<McpClientTool>> ListToolsAsync(string serverName, CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(serverName, out var tools))
        {
            throw new InvalidOperationException($"MCP server '{serverName}' is not connected");
        }

        return Task.FromResult(tools);
    }

    public async Task<IReadOnlyList<AITool>> GetAllAIToolsAsync(CancellationToken cancellationToken = default)
    {
        var allTools = new List<AITool>();

        foreach (var (serverName, tools) in _tools)
        {
            allTools.AddRange(tools.Cast<AITool>());
        }

        return allTools;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var serverName in _clients.Keys.ToList())
        {
            await DisconnectAsync(serverName);
        }

        _clients.Clear();
        _tools.Clear();

        GC.SuppressFinalize(this);
    }

    private class McpClientWrapper
    {
        public ModelContextProtocol.Client.McpClient Client { get; }
        public StdioClientTransport Transport { get; }

        public McpClientWrapper(ModelContextProtocol.Client.McpClient client, StdioClientTransport transport)
        {
            Client = client;
            Transport = transport;
        }
    }
}
