namespace NanoBot.Core.Messages;

/// <summary>
/// 消息元数据
/// </summary>
public record MessageMetadata
{
    /// <summary>
    /// Token 使用情况
    /// </summary>
    public TokenUsage? Tokens { get; init; }

    /// <summary>
    /// 成本信息
    /// </summary>
    public CostInfo? Cost { get; init; }

    /// <summary>
    /// 模型信息
    /// </summary>
    public ModelInfo? Model { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public ErrorInfo? Error { get; init; }

    /// <summary>
    /// 自定义元数据
    /// </summary>
    public Dictionary<string, object>? Custom { get; init; }
}

/// <summary>
/// Token 使用情况
/// </summary>
public record TokenUsage
{
    /// <summary>
    /// 输入 Token 数
    /// </summary>
    public int Input { get; init; }

    /// <summary>
    /// 输出 Token 数
    /// </summary>
    public int Output { get; init; }

    /// <summary>
    /// 推理 Token 数（如 Claude 的扩展思考）
    /// </summary>
    public int? Reasoning { get; init; }

    /// <summary>
    /// 总 Token 数
    /// </summary>
    public int? Total => Input + Output + (Reasoning ?? 0);

    /// <summary>
    /// 缓存 Token 使用情况
    /// </summary>
    public CacheTokenUsage? Cache { get; init; }
}

/// <summary>
/// 缓存 Token 使用情况
/// </summary>
public record CacheTokenUsage
{
    /// <summary>
    /// 读取缓存的 Token 数
    /// </summary>
    public int Read { get; init; }

    /// <summary>
    /// 写入缓存的 Token 数
    /// </summary>
    public int Write { get; init; }
}

/// <summary>
/// 成本信息
/// </summary>
public record CostInfo
{
    /// <summary>
    /// 输入成本
    /// </summary>
    public decimal InputCost { get; set; }

    /// <summary>
    /// 输出成本
    /// </summary>
    public decimal OutputCost { get; set; }

    /// <summary>
    /// 总成本
    /// </summary>
    public decimal? TotalCost { get; set; }
}

/// <summary>
/// 模型信息
/// </summary>
public record ModelInfo
{
    /// <summary>
    /// 提供商 ID
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// 模型 ID
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// 模型版本
    /// </summary>
    public string? Version { get; init; }
}

/// <summary>
/// 错误信息
/// </summary>
public record ErrorInfo
{
    /// <summary>
    /// 错误名称/类型
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 错误代码
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// 详细错误信息
    /// </summary>
    public Dictionary<string, object>? Details { get; init; }
}
