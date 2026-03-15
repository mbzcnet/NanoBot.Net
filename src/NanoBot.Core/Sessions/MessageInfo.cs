namespace NanoBot.Core.Sessions;

public class MessageInfo
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public List<AttachmentInfo> Attachments { get; set; } = new();
    public ToolCallInfo? ToolCall { get; set; }
    public List<ToolExecutionInfo> ToolExecutions { get; set; } = new();
    public int SourceIndex { get; set; } = -1;
    public int? RetryFromIndex { get; set; }
    public string? RetryPrompt { get; set; }
    
    /// <summary>
    /// 有序的 Part 列表，用于 OpenCode 风格的交错渲染（文本与工具按出现顺序显示）
    /// 若为空则回退到传统的先 ToolExecutions 后 Content 渲染方式
    /// </summary>
    public List<MessagePartInfo> Parts { get; set; } = new();
}

public class ToolExecutionInfo
{
    public string CallId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
    public string Output { get; set; } = string.Empty;
    public bool IsError { get; set; }
}

/// <summary>
/// 消息 Part 信息，用于有序渲染
/// </summary>
public class MessagePartInfo
{
    /// <summary>
    /// Part 类型：text（文本）、tool_call（工具调用）、tool_result（工具结果）
    /// </summary>
    public string Type { get; set; } = "text";
    
    /// <summary>
    /// 文本内容（当 Type 为 text 时）
    /// </summary>
    public string? Text { get; set; }
    
    /// <summary>
    /// 工具调用 ID（当 Type 为 tool_call 或 tool_result 时）
    /// </summary>
    public string? CallId { get; set; }
    
    /// <summary>
    /// 工具名称（当 Type 为 tool_call 或 tool_result 时）
    /// </summary>
    public string? ToolName { get; set; }
    
    /// <summary>
    /// 工具参数 JSON（当 Type 为 tool_call 时）
    /// </summary>
    public string? Arguments { get; set; }
    
    /// <summary>
    /// 工具输出（当 Type 为 tool_result 时）
    /// </summary>
    public string? Output { get; set; }
    
    /// <summary>
    /// 是否为错误输出
    /// </summary>
    public bool IsError { get; set; }
}
