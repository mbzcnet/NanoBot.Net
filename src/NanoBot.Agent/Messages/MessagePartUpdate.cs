using NanoBot.Core.Messages;

namespace NanoBot.Agent.Messages;

/// <summary>
/// 消息 Part 更新事件类型
/// </summary>
public enum UpdateType
{
    /// <summary>
    /// 消息开始创建
    /// </summary>
    MessageStarted,

    /// <summary>
    /// 添加了新的 Part
    /// </summary>
    PartAdded,

    /// <summary>
    /// Part 内容更新
    /// </summary>
    PartUpdated,

    /// <summary>
    /// 工具状态变更
    /// </summary>
    ToolStateChanged,

    /// <summary>
    /// 消息完成
    /// </summary>
    MessageCompleted,

    /// <summary>
    /// 消息错误
    /// </summary>
    MessageError
}

/// <summary>
/// 消息 Part 更新事件
/// </summary>
public record MessagePartUpdate
{
    /// <summary>
    /// 消息 ID
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Part ID（可选）
    /// </summary>
    public string? PartId { get; init; }

    /// <summary>
    /// 更新类型
    /// </summary>
    public required UpdateType Type { get; init; }

    /// <summary>
    /// 更新数据
    /// </summary>
    public required object Data { get; init; }

    /// <summary>
    /// 更新时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 工具执行结果
/// </summary>
public record ToolExecutionResult
{
    /// <summary>
    /// 输出内容
    /// </summary>
    public required string Output { get; init; }

    /// <summary>
    /// 显示标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 附件列表
    /// </summary>
    public List<FileAttachment> Attachments { get; init; } = new();

    /// <summary>
    /// 执行元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// 消息流选项
/// </summary>
public class MessageStreamOptions
{
    /// <summary>
    /// 工具执行前回调
    /// </summary>
    public Func<ToolPart, Task>? OnToolExecuting { get; init; }

    /// <summary>
    /// 工具执行后回调
    /// </summary>
    public Func<ToolPart, ToolExecutionResult, Task>? OnToolExecuted { get; init; }

    /// <summary>
    /// Part 添加回调
    /// </summary>
    public Func<MessagePart, Task>? OnPartAdded { get; init; }

    /// <summary>
    /// 消息完成回调
    /// </summary>
    public Func<MessageWithParts, Task>? OnCompleted { get; init; }
}
