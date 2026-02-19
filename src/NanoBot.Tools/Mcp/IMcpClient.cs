using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace NanoBot.Tools.Mcp;

public interface IMcpClient : IAsyncDisposable
{
    IReadOnlyList<string> ConnectedServers { get; }

    Task<IList<McpClientTool>> ListToolsAsync(string serverName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AITool>> GetAllAIToolsAsync(CancellationToken cancellationToken = default);

    Task ConnectAsync(string serverName, McpServerConfig config, CancellationToken cancellationToken = default);

    Task DisconnectAsync(string serverName, CancellationToken cancellationToken = default);
}

public record McpServerConfig
{
    public required string Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
    public string? Cwd { get; init; }
    public string? Url { get; init; }
}
