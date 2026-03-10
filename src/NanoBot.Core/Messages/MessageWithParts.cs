namespace NanoBot.Core.Messages;

/// <summary>
/// 包含 Part 的消息容器
/// </summary>
public record MessageWithParts
{
    /// <summary>
    /// 消息唯一标识符
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 所属会话 ID
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 消息角色（user/assistant）
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// 消息元数据
    /// </summary>
    public MessageMetadata? Metadata { get; init; }

    /// <summary>
    /// 消息 Part 列表
    /// </summary>
    public List<MessagePart> Parts { get; init; } = new();

    /// <summary>
    /// 父消息 ID（用于 threading）
    /// </summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// 变体标识（用于多版本消息）
    /// </summary>
    public string? Variant { get; init; }

    /// <summary>
    /// 获取所有工具 Part
    /// </summary>
    public IEnumerable<ToolPart> GetToolParts() => Parts.OfType<ToolPart>();

    /// <summary>
    /// 获取文本内容（第一个 TextPart 的内容）
    /// </summary>
    public string? GetTextContent() =>
        Parts.OfType<TextPart>()
             .FirstOrDefault(p => !p.Ignored)
             ?.Text;

    /// <summary>
    /// 获取所有文本内容
    /// </summary>
    public string GetAllTextContent() =>
        string.Join("\n", Parts.OfType<TextPart>()
                              .Where(p => !p.Ignored)
                              .Select(p => p.Text));

    /// <summary>
    /// 获取文件 Part
    /// </summary>
    public IEnumerable<FilePart> GetFileParts() => Parts.OfType<FilePart>();

    /// <summary>
    /// 获取推理 Part
    /// </summary>
    public IEnumerable<ReasoningPart> GetReasoningParts() => Parts.OfType<ReasoningPart>();

    /// <summary>
    /// 添加 Part
    /// </summary>
    public void AddPart(MessagePart part)
    {
        Parts.Add(part);
    }

    /// <summary>
    /// 更新 Part（通过 ID 替换）
    /// </summary>
    public bool UpdatePart(string partId, MessagePart newPart)
    {
        var index = Parts.FindIndex(p => p.Id == partId);
        if (index < 0) return false;

        Parts[index] = newPart;
        return true;
    }

    /// <summary>
    /// 查找指定类型的 Part
    /// </summary>
    public IEnumerable<T> FindParts<T>() where T : MessagePart =>
        Parts.OfType<T>();

    /// <summary>
    /// 检查消息是否包含指定类型的 Part
    /// </summary>
    public bool HasPart<T>() where T : MessagePart =>
        Parts.OfType<T>().Any();

    /// <summary>
    /// 获取最后更新时间
    /// </summary>
    public DateTimeOffset GetLastUpdateTime() =>
        Parts.Max(p => p.UpdatedAt ?? p.CreatedAt);
}
