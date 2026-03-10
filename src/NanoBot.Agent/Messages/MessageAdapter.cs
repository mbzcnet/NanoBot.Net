using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NanoBot.Core.Bus;
using NanoBot.Core.Messages;

namespace NanoBot.Agent.Messages;

/// <summary>
/// 消息格式适配器 - 在旧消息格式和新 Part 格式之间转换
/// </summary>
public static class MessageAdapter
{
    /// <summary>
    /// 将 InboundMessage 转换为 MessageWithParts
    /// </summary>
    public static MessageWithParts ToMessageWithParts(InboundMessage message)
    {
        var parts = new List<MessagePart>();
        var messageId = Guid.NewGuid().ToString();

        // 转换文本内容为 TextPart
        if (!string.IsNullOrEmpty(message.Content))
        {
            parts.Add(new TextPart
            {
                Id = Guid.NewGuid().ToString(),
                MessageId = messageId,
                SessionId = message.SessionKey,
                Text = message.Content
            });
        }

        // 转换媒体文件为 FilePart
        foreach (var mediaPath in message.Media ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(mediaPath))
                continue;

            parts.Add(new FilePart
            {
                Id = Guid.NewGuid().ToString(),
                MessageId = messageId,
                SessionId = message.SessionKey,
                FilePath = mediaPath
            });
        }

        return new MessageWithParts
        {
            Id = messageId,
            SessionId = message.SessionKey,
            Role = "user",
            Parts = parts,
            Metadata = message.Metadata != null
                ? new MessageMetadata { Custom = new Dictionary<string, object>(message.Metadata) }
                : null
        };
    }

    /// <summary>
    /// 将 MessageWithParts 转换为 OutboundMessage
    /// </summary>
    public static OutboundMessage ToOutboundMessage(
        MessageWithParts message,
        string channel,
        string chatId)
    {
        var textContent = message.GetAllTextContent();
        var files = message.GetFileParts()
                          .Select(f => f.FilePath)
                          .ToList();

        return new OutboundMessage
        {
            Channel = channel,
            ChatId = chatId,
            Content = textContent,
            Media = files,
            Metadata = message.Metadata?.Custom
        };
    }

    /// <summary>
    /// 从 AgentResponse 创建 MessageWithParts（助手消息）
    /// </summary>
    public static MessageWithParts FromAgentResponse(
        AgentResponse response,
        string sessionId,
        string? parentId = null)
    {
        var messageId = Guid.NewGuid().ToString();
        var parts = new List<MessagePart>();

        foreach (var message in response.Messages)
        {
            // 提取文本内容
            var text = message.Text;
            if (!string.IsNullOrEmpty(text))
            {
                parts.Add(new TextPart
                {
                    Id = Guid.NewGuid().ToString(),
                    MessageId = messageId,
                    SessionId = sessionId,
                    Text = text
                });
            }

            // 这里可以扩展提取其他类型的 Part（如 FunctionCallContent 转为 ToolPart）
        }

        return new MessageWithParts
        {
            Id = messageId,
            SessionId = sessionId,
            Role = "assistant",
            ParentId = parentId,
            Parts = parts
        };
    }

    /// <summary>
    /// 创建带 Token 使用信息的助手消息
    /// </summary>
    public static MessageWithParts FromAgentResponse(
        AgentResponse response,
        string sessionId,
        TokenUsage? tokenUsage,
        ModelInfo? modelInfo,
        string? parentId = null)
    {
        var message = FromAgentResponse(response, sessionId, parentId);

        if (tokenUsage != null || modelInfo != null)
        {
            message = message with
            {
                Metadata = new MessageMetadata
                {
                    Tokens = tokenUsage,
                    Model = modelInfo
                }
            };
        }

        return message;
    }

    /// <summary>
    /// 从 ChatMessage 创建 Part 列表
    /// </summary>
    public static IEnumerable<MessagePart> ExtractPartsFromChatMessage(
        ChatMessage message,
        string messageId,
        string sessionId)
    {
        var parts = new List<MessagePart>();

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent textContent:
                    if (!string.IsNullOrEmpty(textContent.Text))
                    {
                        parts.Add(new TextPart
                        {
                            Id = Guid.NewGuid().ToString(),
                            MessageId = messageId,
                            SessionId = sessionId,
                            Text = textContent.Text
                        });
                    }
                    break;

                case FunctionCallContent functionCall:
                    parts.Add(new ToolPart
                    {
                        Id = Guid.NewGuid().ToString(),
                        MessageId = messageId,
                        SessionId = sessionId,
                        CallId = functionCall.CallId ?? Guid.NewGuid().ToString(),
                        ToolName = functionCall.Name,
                        Input = functionCall.Arguments?.ToDictionary(
                            kvp => kvp.Key,
                            kvp => (object?)kvp.Value) ?? new Dictionary<string, object?>(),
                        State = new PendingToolState()
                    });
                    break;
            }
        }

        return parts;
    }
}
