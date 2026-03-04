namespace NanoBot.Core.Sessions;

public class MessageInfo
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public List<AttachmentInfo> Attachments { get; set; } = new();
}
