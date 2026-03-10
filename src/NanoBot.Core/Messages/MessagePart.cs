namespace NanoBot.Core.Messages;

/// <summary>
/// 消息 Part 的抽象基类，支持多态序列化
/// </summary>
public abstract record MessagePart
{
    /// <summary>
    /// Part 的唯一标识符
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 所属消息的 ID
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// 所属会话的 ID
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Part 类型标识，用于反序列化
    /// </summary>
    public abstract string Type { get; }

    /// <summary>
    /// Part 创建时间
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Part 更新时间
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}