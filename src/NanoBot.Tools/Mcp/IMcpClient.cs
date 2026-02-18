using System.Text.Json;

namespace NanoBot.Tools.Mcp;

/// <summary>
/// MCP 客户端接口
/// </summary>
public interface IMcpClient
{
    /// <summary>
    /// 连接到 MCP 服务器
    /// </summary>
    Task ConnectAsync(string serverName, McpServerConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(string serverName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取服务器提供的工具列表
    /// </summary>
    Task<IReadOnlyList<McpTool>> ListToolsAsync(string serverName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 调用 MCP 工具
    /// </summary>
    Task<McpToolResult> CallToolAsync(
        string serverName,
        string toolName,
        Dictionary<string, object> arguments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已连接的服务器
    /// </summary>
    IReadOnlyList<string> ConnectedServers { get; }

    /// <summary>
    /// 获取所有可用工具
    /// </summary>
    IReadOnlyList<McpTool> GetAllTools();
}

/// <summary>
/// MCP 服务器配置
/// </summary>
public record McpServerConfig
{
    /// <summary>
    /// 启动命令
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// 命令参数
    /// </summary>
    public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 环境变量
    /// </summary>
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// 工作目录
    /// </summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// 服务器 URL（用于 HTTP 模式）
    /// </summary>
    public string? Url { get; init; }
}

/// <summary>
/// MCP 工具定义
/// </summary>
public record McpTool
{
    /// <summary>
    /// 所属服务器名称
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// 工具名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 工具描述
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 输入 Schema
    /// </summary>
    public required JsonElement InputSchema { get; init; }
}

/// <summary>
/// MCP 工具执行结果
/// </summary>
public record McpToolResult
{
    /// <summary>
    /// 结果内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 是否为错误
    /// </summary>
    public bool IsError { get; init; }
}
