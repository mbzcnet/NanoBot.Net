namespace NanoBot.Core.Messages;

/// <summary>
/// 工具状态抽象基类
/// </summary>
public abstract record ToolState
{
    /// <summary>
    /// 状态类型标识
    /// </summary>
    public abstract string Status { get; }

    /// <summary>
    /// 开始执行时间
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// 执行完成时间
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// 执行持续时间
    /// </summary>
    public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;
}

/// <summary>
/// 等待执行状态
/// </summary>
public record PendingToolState : ToolState
{
    public override string Status => "pending";

    /// <summary>
    /// 原始 LLM 输出（用于调试）
    /// </summary>
    public string? RawInput { get; init; }
}

/// <summary>
/// 执行中状态
/// </summary>
public record RunningToolState : ToolState
{
    public override string Status => "running";

    /// <summary>
    /// 执行标题（显示给用户）
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// 执行元数据（可实时更新）
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// 执行完成状态
/// </summary>
public record CompletedToolState : ToolState
{
    public override string Status => "completed";

    /// <summary>
    /// 输出内容
    /// </summary>
    public required string Output { get; init; }

    /// <summary>
    /// 执行标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 附件列表
    /// </summary>
    public List<FileAttachment> Attachments { get; init; } = new();

    /// <summary>
    /// 最终元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// 执行错误状态
/// </summary>
public record ErrorToolState : ToolState
{
    public override string Status => "error";

    /// <summary>
    /// 错误消息
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// 错误代码
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// 堆栈跟踪
    /// </summary>
    public string? StackTrace { get; init; }

    /// <summary>
    /// 是否可重试
    /// </summary>
    public bool Retryable { get; init; } = true;
}

/// <summary>
/// 文件附件
/// </summary>
public record FileAttachment
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 文件内容
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// MIME 类型
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string? FileName => Path.GetFileName(FilePath);
}
