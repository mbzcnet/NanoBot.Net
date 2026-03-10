namespace NanoBot.Core.Messages;

/// <summary>
/// 推理过程 Part（用于显示模型的思考过程）
/// </summary>
public record ReasoningPart : MessagePart
{
    public override string Type => "reasoning";

    /// <summary>
    /// 推理内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 是否是总结性推理（可折叠）
    /// </summary>
    public bool Summary { get; init; }

    /// <summary>
    /// 推理类型
    /// </summary>
    public ReasoningType ReasoningType { get; init; } = ReasoningType.General;
}

/// <summary>
/// 推理类型
/// </summary>
public enum ReasoningType
{
    /// <summary>
    /// 一般推理
    /// </summary>
    General,

    /// <summary>
    /// 规划
    /// </summary>
    Planning,

    /// <summary>
    /// 分析
    /// </summary>
    Analysis,

    /// <summary>
    /// 验证
    /// </summary>
    Verification,

    /// <summary>
    /// 反思
    /// </summary>
    Reflection
}
