using NanoBot.Core.Messages;
using NanoBot.Core.Sessions;

namespace NanoBot.WebUI.Components.Shared;

/// <summary>
/// UI-layer message model for the Chat page.
/// Wraps <see cref="MessageInfo"/> with additional client-side state.
/// </summary>
public class ChatMessage
{
    public string? Id { get; set; }
    public int SourceIndex { get; set; } = -1;
    public int? RetryFromIndex { get; set; }
    public string? RetryPrompt { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public ToolCallInfo? ToolCall { get; set; }
    public List<ChatToolExecution> ToolExecutions { get; set; } = new();
    public List<MessagePart>? Parts { get; set; }

    /// <summary>
    /// Ordered parts loaded from the session (text, tool_call, tool_result).
    /// Used for OpenCode-style interleaved rendering.
    /// </summary>
    public List<MessagePartInfo>? SessionParts { get; set; }

    /// <summary>
    /// Whether this message has valid SessionParts for interleaved rendering.
    /// </summary>
    public bool HasSessionParts => SessionParts != null && SessionParts.Count > 0;

    /// <summary>
    /// Gets or creates a part list for backward compatibility.
    /// </summary>
    public List<MessagePart> GetOrCreateParts(string sessionId)
    {
        if (Parts != null && Parts.Count > 0)
            return Parts;

        var parts = new List<MessagePart>();
        if (!string.IsNullOrEmpty(Content))
        {
            parts.Add(new TextPart
            {
                Id = Guid.NewGuid().ToString(),
                MessageId = Id ?? Guid.NewGuid().ToString(),
                SessionId = sessionId,
                Text = Content
            });
        }
        return parts;
    }
}

/// <summary>
/// Client-side tool execution for a chat message.
/// </summary>
public class ChatToolExecution
{
    public string CallId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
    public string Output { get; set; } = string.Empty;
    public bool IsError { get; set; }
}

/// <summary>
/// Profile option displayed in the model selector dropdown.
/// </summary>
public sealed record ProfileOption(string ProfileId, string Display);
