using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NanoBot.Tools.Mcp;

/// <summary>
/// MCP 客户端实现
/// </summary>
public class McpClient : IMcpClient, IDisposable
{
    private readonly ILogger<McpClient>? _logger;
    private readonly ConcurrentDictionary<string, McpServerConnection> _connections = new();
    private readonly ConcurrentDictionary<string, McpTool> _allTools = new();
    private bool _disposed;

    public McpClient(ILogger<McpClient>? logger = null)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> ConnectedServers => _connections.Keys.ToList();

    public IReadOnlyList<McpTool> GetAllTools() => _allTools.Values.ToList();

    public async Task ConnectAsync(string serverName, McpServerConfig config, CancellationToken cancellationToken = default)
    {
        if (_connections.ContainsKey(serverName))
        {
            _logger?.LogWarning("MCP server '{ServerName}' is already connected", serverName);
            return;
        }

        try
        {
            _logger?.LogInformation("Connecting to MCP server '{ServerName}'...", serverName);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = config.Command,
                    Arguments = string.Join(" ", config.Args),
                    WorkingDirectory = config.Cwd ?? Environment.CurrentDirectory,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (config.Env.Count > 0)
            {
                foreach (var (key, value) in config.Env)
                {
                    process.StartInfo.Environment[key] = value;
                }
            }

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger?.LogError("MCP server '{ServerName}' stderr: {Error}", serverName, e.Data);
                }
            };

            process.Start();
            process.BeginErrorReadLine();

            var connection = new McpServerConnection
            {
                ServerName = serverName,
                Process = process,
                Config = config
            };

            _connections[serverName] = connection;

            await InitializeAsync(connection, cancellationToken);

            var tools = await ListToolsInternalAsync(connection, cancellationToken);
            foreach (var tool in tools)
            {
                var fullTool = tool with { ServerName = serverName };
                _allTools[$"{serverName}_{tool.Name}"] = fullTool;
            }

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
        if (_connections.TryRemove(serverName, out var connection))
        {
            _logger?.LogInformation("Disconnecting from MCP server '{ServerName}'...", serverName);

            try
            {
                if (!connection.Process.HasExited)
                {
                    connection.Process.Kill(true);
                    await connection.Process.WaitForExitAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error disconnecting from MCP server '{ServerName}'", serverName);
            }
            finally
            {
                connection.Process.Dispose();

                var toolsToRemove = _allTools.Where(t => t.Key.StartsWith($"{serverName}_")).Select(t => t.Key).ToList();
                foreach (var key in toolsToRemove)
                {
                    _allTools.TryRemove(key, out _);
                }
            }
        }
    }

    public Task<IReadOnlyList<McpTool>> ListToolsAsync(string serverName, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(serverName, out var connection))
        {
            throw new InvalidOperationException($"MCP server '{serverName}' is not connected");
        }

        return ListToolsInternalAsync(connection, cancellationToken);
    }

    public async Task<McpToolResult> CallToolAsync(
        string serverName,
        string toolName,
        Dictionary<string, object> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(serverName, out var connection))
        {
            throw new InvalidOperationException($"MCP server '{serverName}' is not connected");
        }

        try
        {
            var request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Id = Guid.NewGuid().ToString("N"),
                Method = "tools/call",
                Params = new Dictionary<string, object>
                {
                    ["name"] = toolName,
                    ["arguments"] = arguments
                }
            };

            var response = await SendRequestAsync(connection, request, cancellationToken);

            if (response.Error != null)
            {
                return new McpToolResult
                {
                    Content = $"Error: {response.Error.Message}",
                    IsError = true
                };
            }

            var content = response.Result?.TryGetValue("content", out var c) == true
                ? string.Join("\n", c.EnumerateArray().Select(x => x.GetString() ?? ""))
                : "(no output)";

            return new McpToolResult
            {
                Content = content,
                IsError = false
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error calling MCP tool '{ToolName}' on server '{ServerName}'", toolName, serverName);
            return new McpToolResult
            {
                Content = $"Error: {ex.Message}",
                IsError = true
            };
        }
    }

    private async Task InitializeAsync(McpServerConnection connection, CancellationToken cancellationToken)
    {
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = Guid.NewGuid().ToString("N"),
            Method = "initialize",
            Params = new Dictionary<string, object>
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new Dictionary<string, object>(),
                ["clientInfo"] = new Dictionary<string, string>
                {
                    ["name"] = "NanoBot.Net",
                    ["version"] = "1.0.0"
                }
            }
        };

        var response = await SendRequestAsync(connection, request, cancellationToken);

        if (response.Error != null)
        {
            throw new InvalidOperationException($"MCP initialization failed: {response.Error.Message}");
        }

        var notification = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = "notifications/initialized"
        };

        await SendNotificationAsync(connection, notification, cancellationToken);
    }

    private async Task<IReadOnlyList<McpTool>> ListToolsInternalAsync(McpServerConnection connection, CancellationToken cancellationToken)
    {
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Id = Guid.NewGuid().ToString("N"),
            Method = "tools/list"
        };

        var response = await SendRequestAsync(connection, request, cancellationToken);

        if (response.Error != null)
        {
            throw new InvalidOperationException($"Failed to list tools: {response.Error.Message}");
        }

        var tools = new List<McpTool>();

        if (response.Result?.TryGetValue("tools", out var toolsElement) == true)
        {
            foreach (var tool in toolsElement.EnumerateArray())
            {
                var name = tool.GetProperty("name").GetString() ?? "";
                var description = tool.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
                var inputSchema = tool.TryGetProperty("inputSchema", out var schema) ? schema : JsonDocument.Parse("{}").RootElement;

                tools.Add(new McpTool
                {
                    ServerName = connection.ServerName,
                    Name = name,
                    Description = description,
                    InputSchema = inputSchema
                });
            }
        }

        return tools;
    }

    private async Task<JsonRpcResponse> SendRequestAsync(McpServerConnection connection, JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var requestJson = JsonSerializer.Serialize(request);
        _logger?.LogDebug("MCP Request: {Request}", requestJson);

        await connection.Process.StandardInput.WriteLineAsync(requestJson.AsMemory(), cancellationToken);
        await connection.Process.StandardInput.FlushAsync(cancellationToken);

        var responseLine = await connection.Process.StandardOutput.ReadLineAsync(cancellationToken);

        if (string.IsNullOrEmpty(responseLine))
        {
            throw new InvalidOperationException("Empty response from MCP server");
        }

        _logger?.LogDebug("MCP Response: {Response}", responseLine);

        return JsonSerializer.Deserialize<JsonRpcResponse>(responseLine)
            ?? throw new InvalidOperationException("Failed to parse MCP response");
    }

    private async Task SendNotificationAsync(McpServerConnection connection, JsonRpcRequest notification, CancellationToken cancellationToken)
    {
        var notificationJson = JsonSerializer.Serialize(notification);
        _logger?.LogDebug("MCP Notification: {Notification}", notificationJson);

        await connection.Process.StandardInput.WriteLineAsync(notificationJson.AsMemory(), cancellationToken);
        await connection.Process.StandardInput.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var serverName in _connections.Keys.ToList())
        {
            DisconnectAsync(serverName).GetAwaiter().GetResult();
        }

        _connections.Clear();
        _allTools.Clear();

        GC.SuppressFinalize(this);
    }

    private class McpServerConnection
    {
        public required string ServerName { get; init; }
        public required Process Process { get; init; }
        public required McpServerConfig Config { get; init; }
    }

    private class JsonRpcRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("params")]
        public Dictionary<string, object>? Params { get; set; }
    }

    private class JsonRpcResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("result")]
        public Dictionary<string, JsonElement>? Result { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public JsonRpcError? Error { get; set; }
    }

    private class JsonRpcError
    {
        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public int Code { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }
}
