namespace NanoBot.Core.Messages;

/// <summary>
/// 文本内容 Part
/// </summary>
public record TextPart : MessagePart
{
    public override string Type => "text";

    /// <summary>
    /// 文本内容
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 是否由系统生成（非 LLM 生成）
    /// </summary>
    public bool Synthetic { get; init; }

    /// <summary>
    /// 是否被忽略（不计入上下文）
    /// </summary>
    public bool Ignored { get; init; }

    /// <summary>
    /// 文本对应的时间范围（用于语音转文本等场景）
    /// </summary>
    public TimeRange? Time { get; init; }
}

/// <summary>
/// 时间范围
/// </summary>
public record TimeRange
{
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
}
