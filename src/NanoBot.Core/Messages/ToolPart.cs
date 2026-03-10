namespace NanoBot.Core.Messages;

/// <summary>
/// 工具调用 Part
/// </summary>
public record ToolPart : MessagePart
{
    public override string Type => "tool";

    /// <summary>
    /// 工具调用 ID（用于关联请求和响应）
    /// </summary>
    public required string CallId { get; init; }

    /// <summary>
    /// 工具名称
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// 输入参数
    /// </summary>
    public required Dictionary<string, object?> Input { get; init; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public required ToolState State { get; init; }

    /// <summary>
    /// 创建时间（执行开始时间）
    /// </summary>
    public DateTimeOffset? ExecutedAt { get; init; }

    /// <summary>
    /// 工具分组（用于上下文工具分组显示）
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// 获取输入参数的字符串表示
    /// </summary>
    public string GetInputSummary()
    {
        if (Input.TryGetValue("path", out var path) && path != null)
            return Path.GetFileName(path.ToString()) ?? path.ToString()!;

        if (Input.TryGetValue("query", out var query) && query != null)
        {
            var queryStr = query.ToString()!;
            return queryStr.Length > 30 ? queryStr[..30] + "..." : queryStr;
        }

        if (Input.TryGetValue("url", out var url) && url != null)
            return url.ToString()!;

        return $"{Input.Count} parameters";
    }
}
