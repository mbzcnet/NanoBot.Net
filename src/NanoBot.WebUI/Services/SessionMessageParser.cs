using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Sessions;
using NanoBot.Core.Storage;
using NanoBot.Core.Workspace;

namespace NanoBot.WebUI.Services;

/// <summary>
/// Parses session messages from .jsonl session files and normalizes content.
/// </summary>
public sealed class SessionMessageParser
{
    private readonly IWorkspaceManager _workspace;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<SessionMessageParser> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SessionMessageParser(
        ILogger<SessionMessageParser> logger,
        IWorkspaceManager workspace,
        IFileStorageService fileStorage)
    {
        _logger = logger;
        _workspace = workspace;
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// Parses all messages from a .jsonl session file and returns consolidated messages.
    /// </summary>
    public async Task<List<MessageInfo>> ParseMessagesAsync(string sessionId)
    {
        try
        {
            var sessionsPath = _workspace.GetSessionsPath();
            var sessionFile = Path.Combine(sessionsPath, $"chat_{sessionId}.jsonl");

            if (!File.Exists(sessionFile))
                return new List<MessageInfo>();

            var lines = await File.ReadAllLinesAsync(sessionFile);
            var messages = ReadMessagesFromJsonLines(lines, sessionId);
            return ConsolidateMessages(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing messages for session {SessionId}", sessionId);
            return new List<MessageInfo>();
        }
    }

    private List<MessageInfo> ReadMessagesFromJsonLines(string[] lines, string sessionId)
    {
        var messagesList = new List<MessageInfo>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var msg = JsonSerializer.Deserialize<JsonElement>(line);

                // Skip metadata lines
                if (msg.TryGetProperty("_type", out var typeElement) && typeElement.GetString() == "metadata")
                    continue;

                var role = "user";
                var content = string.Empty;
                var timestamp = DateTime.Now;
                var attachments = new List<AttachmentInfo>();
                ToolCallInfo? toolCallInfo = null;
                var toolExecutions = new List<ToolExecutionInfo>();
                var parts = new List<MessagePartInfo>();

                if (msg.TryGetProperty("role", out var roleElement))
                    role = roleElement.GetString()?.ToLower() ?? "user";

                if (msg.TryGetProperty("timestamp", out var timestampElement) &&
                    timestampElement.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(timestampElement.GetString(), out var parsedTimestamp))
                    timestamp = parsedTimestamp;

                if (msg.TryGetProperty("content", out var contentElement))
                {
                    if (contentElement.ValueKind == JsonValueKind.String)
                        content = contentElement.GetString() ?? string.Empty;
                    else if (contentElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in contentElement.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                                content += item.GetString();
                            else if (item.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                                content += textElement.GetString() ?? string.Empty;
                        }
                    }
                }

                // Parse tool_calls
                if (msg.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var call in toolCallsElement.EnumerateArray())
                    {
                        if (!call.TryGetProperty("function", out var functionElement))
                            continue;

                        var functionName = functionElement.TryGetProperty("name", out var nameElement)
                            ? nameElement.GetString()
                            : null;
                        var argsString = functionElement.TryGetProperty("arguments", out var argsElement)
                            ? argsElement.GetString()
                            : null;

                        var callId = call.TryGetProperty("id", out var idElement)
                            ? idElement.GetString() ?? string.Empty
                            : string.Empty;

                        if (!string.IsNullOrWhiteSpace(functionName))
                        {
                            toolExecutions.Add(new ToolExecutionInfo
                            {
                                CallId = callId,
                                Name = functionName,
                                Arguments = argsString ?? "{}"
                            });

                            parts.Add(new MessagePartInfo
                            {
                                Type = "tool_call",
                                CallId = callId,
                                ToolName = functionName,
                                Arguments = argsString ?? "{}"
                            });
                        }
                    }

                    var firstCall = toolCallsElement.EnumerateArray().FirstOrDefault();
                    if (firstCall.ValueKind == JsonValueKind.Object &&
                        firstCall.TryGetProperty("function", out var firstFn) &&
                        firstFn.TryGetProperty("name", out var firstName) &&
                        firstFn.TryGetProperty("arguments", out var firstArgs))
                    {
                        toolCallInfo = new ToolCallInfo(
                            firstName.GetString() ?? string.Empty,
                            firstArgs.GetString() ?? "{}",
                            firstCall.TryGetProperty("id", out var firstId) ? firstId.GetString() : null);
                    }
                }

                // Add text part if there's content
                if (!string.IsNullOrWhiteSpace(content))
                    parts.Add(new MessagePartInfo { Type = "text", Text = content });

                // Handle tool result messages
                if (role == "tool")
                {
                    var toolCallId = msg.TryGetProperty("tool_call_id", out var tcIdEl)
                        ? tcIdEl.GetString() ?? string.Empty
                        : string.Empty;
                    var toolName = msg.TryGetProperty("name", out var tnEl)
                        ? tnEl.GetString() ?? string.Empty
                        : string.Empty;

                    var normalizedOutput = NormalizeToolOutput(content);
                    var isError = IsLikelyError(content);

                    parts.Add(new MessagePartInfo
                    {
                        Type = "tool_result",
                        CallId = toolCallId,
                        ToolName = toolName,
                        Output = normalizedOutput,
                        IsError = isError
                    });

                    toolExecutions.Add(new ToolExecutionInfo
                    {
                        CallId = toolCallId,
                        Name = toolName,
                        Arguments = "{}",
                        Output = normalizedOutput,
                        IsError = isError
                    });

                    // Snapshot image extraction
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        if (TryExtractSnapshotMarkdown(content, out var snapshotMarkdown))
                            content = $"![snapshot]({snapshotMarkdown})";
                        content = string.Empty;
                    }
                }

                // Session images attachment
                if (TryExtractSessionImages(msg, out var sessionImages))
                {
                    foreach (var image in sessionImages)
                    {
                        attachments.Add(new AttachmentInfo
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            MessageId = $"{sessionId}_{messagesList.Count}",
                            FileName = string.Empty,
                            FileType = image.ContentType,
                            RelativePath = image.ThumbnailUrl,
                            FileSize = image.FileSize,
                            Url = image.OriginalUrl,
                            Summary = image.Summary
                        });
                    }
                    content = AppendImageSummaries(content, sessionImages);
                }

                if (!string.IsNullOrWhiteSpace(content) || toolCallInfo != null || attachments.Count > 0 || toolExecutions.Count > 0 || parts.Count > 0)
                {
                    var messageIndex = messagesList.Count;
                    messagesList.Add(new MessageInfo
                    {
                        Id = $"{sessionId}_{messageIndex}",
                        SessionId = sessionId,
                        Role = role,
                        Content = content,
                        Timestamp = timestamp,
                        Attachments = attachments,
                        ToolCall = toolCallInfo,
                        ToolExecutions = toolExecutions,
                        SourceIndex = messageIndex,
                        Parts = parts.Count > 0 ? parts : new List<MessagePartInfo>()
                    });
                }
            }
            catch
            {
                // Skip malformed lines
            }
        }

        return messagesList;
    }

    private List<MessageInfo> ConsolidateMessages(List<MessageInfo> messagesList)
    {
        var consolidated = new List<MessageInfo>();
        MessageInfo? currentResponse = null;

        foreach (var msg in messagesList)
        {
            if (msg.Role == "user" || msg.Role == "system")
            {
                consolidated.Add(msg);
                currentResponse = null;
            }
            else
            {
                if (currentResponse != null)
                {
                    // Merge into existing assistant message
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                        currentResponse.Content = string.IsNullOrWhiteSpace(currentResponse.Content)
                            ? msg.Content
                            : $"{currentResponse.Content}\n\n{msg.Content}";

                    currentResponse.Attachments.AddRange(msg.Attachments);

                    if (currentResponse.ToolCall == null && msg.ToolCall != null)
                        currentResponse.ToolCall = msg.ToolCall;

                    if (msg.ToolExecutions.Count > 0)
                        MergeToolExecutions(currentResponse.ToolExecutions, msg.ToolExecutions);

                    if (msg.Parts.Count > 0)
                        currentResponse.Parts.AddRange(msg.Parts);

                    currentResponse.Timestamp = msg.Timestamp;
                    currentResponse.SourceIndex = Math.Max(currentResponse.SourceIndex, msg.SourceIndex);
                }
                else
                {
                    // Tool messages become assistant
                    if (msg.Role == "tool")
                    {
                        msg.Role = "assistant";
                        msg.Content = string.Empty;
                    }

                    var retryCandidate = consolidated.LastOrDefault(m => m.Role == "user");
                    msg.RetryPrompt = retryCandidate?.Content;
                    msg.RetryFromIndex = retryCandidate?.SourceIndex;
                    consolidated.Add(msg);
                    currentResponse = msg;
                }
            }
        }

        return consolidated;
    }

    private static void MergeToolExecutions(List<ToolExecutionInfo> target, List<ToolExecutionInfo> source)
    {
        foreach (var incoming in source)
        {
            var existing = !string.IsNullOrWhiteSpace(incoming.CallId)
                ? target.LastOrDefault(t => string.Equals(t.CallId, incoming.CallId, StringComparison.Ordinal))
                : target.LastOrDefault();

            if (existing == null)
            {
                target.Add(new ToolExecutionInfo
                {
                    CallId = incoming.CallId,
                    Name = incoming.Name,
                    Arguments = incoming.Arguments,
                    Output = incoming.Output,
                    IsError = incoming.IsError
                });
                continue;
            }

            if (!string.IsNullOrWhiteSpace(incoming.CallId) && string.IsNullOrWhiteSpace(existing.CallId))
                existing.CallId = incoming.CallId;

            if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(incoming.Name))
                existing.Name = incoming.Name;

            if ((string.IsNullOrWhiteSpace(existing.Arguments) || existing.Arguments == "{}") &&
                !string.IsNullOrWhiteSpace(incoming.Arguments) && incoming.Arguments != "{}")
                existing.Arguments = incoming.Arguments;

            if (string.IsNullOrWhiteSpace(existing.Output) && !string.IsNullOrWhiteSpace(incoming.Output))
                existing.Output = incoming.Output;
            else if (!string.IsNullOrWhiteSpace(incoming.Output) &&
                     !string.Equals(existing.Output, incoming.Output, StringComparison.Ordinal))
                existing.Output = string.IsNullOrWhiteSpace(existing.Output)
                    ? incoming.Output
                    : $"{existing.Output}\n{incoming.Output}";

            existing.IsError = existing.IsError || incoming.IsError || IsLikelyError(existing.Output);
        }
    }

    public static bool IsLikelyError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Contains("error", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("ERR_", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("SSL", StringComparison.OrdinalIgnoreCase);
    }

    private string NormalizeToolOutput(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return string.Empty;

        var normalized = rawOutput.Trim();
        if (TryExtractSnapshotMarkdown(normalized, out var snapshotMarkdown))
            return string.IsNullOrWhiteSpace(snapshotMarkdown)
                ? normalized
                : $"{normalized}\n\n{snapshotMarkdown}";
        return normalized;
    }

    private bool TryExtractSnapshotMarkdown(string toolContent, out string markdown)
    {
        markdown = string.Empty;
        if (!TryExtractSnapshotImageUrl(toolContent, out var imageUrl) ||
            string.IsNullOrWhiteSpace(imageUrl))
            return false;
        markdown = $"![snapshot]({imageUrl})";
        return true;
    }

    private bool TryExtractSnapshotImageUrl(string toolContent, out string imageUrl)
    {
        imageUrl = string.Empty;
        if (!TryParseToolResultJson(toolContent, out var root))
            return false;

        if (!TryGetJsonString(root, "action", out var action) ||
            string.IsNullOrWhiteSpace(action))
            return false;

        if (!string.Equals(action, "snapshot", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action, "capture", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryGetJsonString(root, "imagePath", out var imagePath) ||
            string.IsNullOrWhiteSpace(imagePath))
            return false;

        var resolved = GetSnapshotUrl(imagePath);
        if (string.IsNullOrWhiteSpace(resolved))
            return false;

        imageUrl = resolved;
        return true;
    }

    private string? GetSnapshotUrl(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        var normalized = imagePath.Replace('\\', '/');
        if (normalized.StartsWith("/api/files/sessions/", StringComparison.OrdinalIgnoreCase))
            return normalized;

        if (Path.IsPathRooted(imagePath))
        {
            var sessionsRoot = _workspace.GetSessionsPath().Replace('\\', '/');
            if (!normalized.StartsWith(sessionsRoot, StringComparison.OrdinalIgnoreCase))
                return $"/api/files/local?path={Uri.EscapeDataString(imagePath)}";
            normalized = normalized[sessionsRoot.Length..].TrimStart('/');
        }

        if (normalized.StartsWith("sessions/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["sessions/".Length..];

        normalized = normalized.TrimStart('/');
        return string.IsNullOrWhiteSpace(normalized) ? null : $"/api/files/sessions/{normalized}";
    }

    private static bool TryGetJsonString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;
            value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.GetRawText();
            return true;
        }
        return false;
    }

    private static bool TryParseToolResultJson(string raw, out JsonElement rootElement)
    {
        rootElement = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var normalized = raw.Trim();

        if (TryParseJsonDirect(normalized, out rootElement))
            return true;

        // Repair \\u0022 -> "
        if (!normalized.Contains("\\u0022", StringComparison.Ordinal))
            return false;

        return TryParseJsonDirect(normalized.Replace("\\u0022", "\""), out rootElement);
    }

    private static bool TryParseJsonDirect(string normalized, out JsonElement rootElement)
    {
        rootElement = default;
        try
        {
            rootElement = JsonSerializer.Deserialize<JsonElement>(normalized);
            if (rootElement.ValueKind == JsonValueKind.String)
            {
                var inner = rootElement.GetString();
                if (!string.IsNullOrWhiteSpace(inner))
                    rootElement = JsonSerializer.Deserialize<JsonElement>(inner);
            }
            return rootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractSessionImages(JsonElement message, out List<SessionImageItem> images)
    {
        images = new List<SessionImageItem>();
        if (!message.TryGetProperty("images", out var imagesElement) ||
            imagesElement.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var image in imagesElement.EnumerateArray())
        {
            var originalUrl = image.TryGetProperty("original_url", out var o) ? o.GetString() ?? string.Empty : string.Empty;
            var thumbnailUrl = image.TryGetProperty("thumbnail_url", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            var summary = image.TryGetProperty("summary", out var s) ? s.GetString() ?? string.Empty : string.Empty;
            var contentType = image.TryGetProperty("content_type", out var ct) ? ct.GetString() ?? string.Empty : string.Empty;
            var fileSize = image.TryGetProperty("file_size", out var fs) && fs.ValueKind == JsonValueKind.Number ? fs.GetInt64() : 0;

            if (string.IsNullOrWhiteSpace(originalUrl) || string.IsNullOrWhiteSpace(thumbnailUrl))
                continue;

            images.Add(new SessionImageItem(originalUrl, thumbnailUrl, summary, 0, 0, contentType, fileSize));
        }

        return images.Count > 0;
    }

    private static string AppendImageSummaries(string content, List<SessionImageItem> images)
    {
        if (images.Count == 0)
            return content;

        var blocks = images
            .Select(image =>
            {
                var summary = string.IsNullOrWhiteSpace(image.Summary) ? "未提供概述" : image.Summary;
                var encoded = WebUtility.HtmlEncode(summary);
                return $"<div class=\"nb-image-summary\">图片概述：{encoded}</div>";
            });

        var summaryBlock = string.Join("\n", blocks);
        return string.IsNullOrWhiteSpace(content) ? summaryBlock : $"{content}\n\n{summaryBlock}";
    }

    private sealed record SessionImageItem(
        string OriginalUrl,
        string ThumbnailUrl,
        string Summary,
        int Width,
        int Height,
        string ContentType,
        long FileSize);
}
