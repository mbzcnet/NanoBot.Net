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
}

public class ToolExecutionInfo
{
    public string CallId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
    public string Output { get; set; } = string.Empty;
    public bool IsError { get; set; }
}
