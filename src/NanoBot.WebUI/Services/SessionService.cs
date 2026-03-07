using NanoBot.Agent;
using NanoBot.Core.Sessions;
using NanoBot.Core.Storage;
using NanoBot.Core.Workspace;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace NanoBot.WebUI.Services;

public class SessionService : ISessionService
{
    private sealed record SessionImageItem(
        string OriginalUrl,
        string ThumbnailUrl,
        string Summary,
        int Width,
        int Height,
        string ContentType,
        long FileSize);

    private readonly ILogger<SessionService> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly IWorkspaceManager _workspace;
    private readonly IFileStorageService _fileStorage;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SessionService(
        ILogger<SessionService> logger,
        ISessionManager sessionManager,
        IWorkspaceManager workspace,
        IFileStorageService fileStorage)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _workspace = workspace;
        _fileStorage = fileStorage;
    }

    public Task<List<SessionInfo>> GetSessionsAsync()
    {
        try
        {
            var sessions = _sessionManager.ListSessions()
                .Where(s => s.Key.StartsWith("webui:"))
                .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt ?? DateTimeOffset.MinValue)
                .Select(s => new SessionInfo
                {
                    Id = s.Key.Replace("webui:", ""),
                    Title = s.Title ?? GenerateDefaultTitle(s.Key),
                    CreatedAt = (s.CreatedAt ?? DateTimeOffset.Now).DateTime,
                    UpdatedAt = (s.UpdatedAt ?? DateTimeOffset.Now).DateTime,
                    ProfileId = s.ProfileId
                })
                .ToList();

            return Task.FromResult(sessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing sessions");
            return Task.FromResult(new List<SessionInfo>());
        }
    }

    public async Task<SessionInfo?> GetSessionAsync(string sessionId)
    {
        try
        {
            var sessionKey = $"webui:{sessionId}";
            var agentSession = _sessionManager.ListSessions()
                .FirstOrDefault(s => s.Key == sessionKey);

            if (agentSession == null)
                return null;

            return new SessionInfo
            {
                Id = sessionId,
                Title = agentSession.Title ?? GenerateDefaultTitle(sessionKey),
                CreatedAt = (agentSession.CreatedAt ?? DateTimeOffset.Now).DateTime,
                UpdatedAt = (agentSession.UpdatedAt ?? DateTimeOffset.Now).DateTime,
                ProfileId = agentSession.ProfileId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<SessionInfo> CreateSessionAsync(string? title = null, string? profileId = null)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var sessionKey = $"webui:{sessionId}";

        try
        {
            var agentSession = await _sessionManager.GetOrCreateSessionAsync(sessionKey);

            var now = DateTime.Now;
            var sessionTitle = title ?? $"会话 {now:MM-dd HH:mm}";

            _sessionManager.SetSessionTitle(sessionKey, sessionTitle);
            if (!string.IsNullOrEmpty(profileId))
            {
                _sessionManager.SetSessionProfileId(sessionKey, profileId);
            }

            // 立即保存会话到文件，确保 ListSessions 可以读取到
            await _sessionManager.SaveSessionAsync(agentSession, sessionKey);

            var session = new SessionInfo
            {
                Id = sessionId,
                Title = sessionTitle,
                CreatedAt = now,
                UpdatedAt = now,
                ProfileId = profileId
            };

            _logger.LogInformation("Created new session: {SessionId}", sessionId);
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session");
            throw;
        }
    }

    public async Task RenameSessionAsync(string sessionId, string newTitle)
    {
        try
        {
            var sessionKey = $"webui:{sessionId}";
            var agentSession = await _sessionManager.GetOrCreateSessionAsync(sessionKey);
            _sessionManager.SetSessionTitle(sessionKey, newTitle);
            await _sessionManager.SaveSessionAsync(agentSession, sessionKey);
            
            _logger.LogInformation("Renamed session {SessionId} to {NewTitle}", sessionId, newTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renaming session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task SetSessionProfileAsync(string sessionId, string profileId)
    {
        try
        {
            var sessionKey = $"webui:{sessionId}";
            var agentSession = await _sessionManager.GetOrCreateSessionAsync(sessionKey);
            _sessionManager.SetSessionProfileId(sessionKey, profileId);
            await _sessionManager.SaveSessionAsync(agentSession, sessionKey);
            _logger.LogInformation("Set session profile {ProfileId} for session {SessionId}", profileId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting profile {ProfileId} for session {SessionId}", profileId, sessionId);
            throw;
        }
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        try
        {
            var sessionKey = $"webui:{sessionId}";
            await _sessionManager.ClearSessionAsync(sessionKey);
            await _fileStorage.DeleteSessionDirectoryAsync(sessionId);

            _logger.LogInformation("Deleted session: {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<List<MessageInfo>> GetMessagesAsync(string sessionId)
    {
        try
        {
            var sessionKey = $"webui:{sessionId}";
            await _sessionManager.GetOrCreateSessionAsync(sessionKey);

            var sessionsPath = _workspace.GetSessionsPath();
            var sessionFile = Path.Combine(sessionsPath, $"{sessionKey.Replace(":", "_")}.jsonl");

            if (!File.Exists(sessionFile))
                return new List<MessageInfo>();

            var messagesList = new List<MessageInfo>();
            var lines = await File.ReadAllLinesAsync(sessionFile);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var msg = JsonSerializer.Deserialize<JsonElement>(line);
                    
                    // 跳过 metadata 行
                    if (msg.TryGetProperty("_type", out var typeElement) && typeElement.GetString() == "metadata")
                        continue;

                    string role = "user";
                    string content = string.Empty;
                    var timestamp = DateTime.Now;
                    var attachments = new List<AttachmentInfo>();
                    
                    // 解析消息 - 支持两种格式
                    // 格式1: { "role": "user", "content": "text" }
                    // 格式2: { "role": "user", "contents": [{"$type": "text", "text": "..."}] }
                    
                    if (msg.TryGetProperty("role", out var roleElement))
                    {
                        role = roleElement.GetString()?.ToLower() ?? "user";
                    }

                    if (msg.TryGetProperty("timestamp", out var timestampElement) &&
                        timestampElement.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(timestampElement.GetString(), out var parsedTimestamp))
                    {
                        timestamp = parsedTimestamp;
                    }

                    if (msg.TryGetProperty("content", out var contentElement))
                    {
                        if (contentElement.ValueKind == JsonValueKind.String)
                        {
                            content = contentElement.GetString() ?? string.Empty;
                        }
                        else if (contentElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in contentElement.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    content += item.GetString();
                                }
                                else if (item.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                                {
                                    content += textElement.GetString() ?? string.Empty;
                                }
                            }
                        }
                    }

                    if (msg.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
                    {
                        var toolCalls = new List<FunctionCallContent>();
                        foreach (var call in toolCallsElement.EnumerateArray())
                        {
                            if (!call.TryGetProperty("function", out var functionElement))
                            {
                                continue;
                            }

                            var functionName = functionElement.TryGetProperty("name", out var nameElement)
                                ? nameElement.GetString()
                                : null;
                            var argsString = functionElement.TryGetProperty("arguments", out var argsElement)
                                ? argsElement.GetString()
                                : null;

                            Dictionary<string, object?>? arguments = null;
                            if (!string.IsNullOrWhiteSpace(argsString))
                            {
                                try
                                {
                                    arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsString);
                                }
                                catch
                                {
                                    arguments = null;
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(functionName))
                            {
                                var callId = call.TryGetProperty("id", out var idElement)
                                    ? idElement.GetString() ?? string.Empty
                                    : string.Empty;
                                toolCalls.Add(new FunctionCallContent(callId, functionName, arguments));
                            }
                        }

                        var toolHint = ToolHintFormatter.FormatToolHint(toolCalls);
                        if (!string.IsNullOrWhiteSpace(toolHint))
                        {
                            var toolHintBlock = WrapToolHintAsHtml(toolHint);
                            content = string.IsNullOrWhiteSpace(content)
                                ? toolHintBlock
                                : $"{content}\n\n{toolHintBlock}";
                        }
                    }

                    if (role == "tool" && !string.IsNullOrWhiteSpace(content))
                    {
                        if (TryExtractSnapshotImageUrl(content, out var snapshotImageUrl))
                        {
                            content = $"![snapshot]({snapshotImageUrl})";
                        }
                        else
                        {
                            content = string.Empty;
                        }
                    }

                    if (TryExtractSessionImages(msg, out var sessionImages))
                    {
                        foreach (var image in sessionImages)
                        {
                            attachments.Add(new AttachmentInfo
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                MessageId = $"{sessionId}_{messagesList.Count}",
                                FileType = image.ContentType,
                                RelativePath = image.ThumbnailUrl,
                                FileSize = image.FileSize,
                                Url = image.OriginalUrl,
                                Summary = image.Summary
                            });
                        }

                        content = AppendImageSummaries(content, sessionImages);
                    }

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        messagesList.Add(new MessageInfo
                        {
                            Id = $"{sessionId}_{messagesList.Count}",
                            SessionId = sessionId,
                            Role = role,
                            Content = content,
                            Timestamp = timestamp,
                            Attachments = attachments
                        });
                    }
                }
                catch
                {
                }
            }

            // 合并连续的 Assistant/Tool 消息，以模拟流式输出时的单一气泡体验
            var consolidatedList = new List<MessageInfo>();
            MessageInfo? currentResponse = null;

            foreach (var msg in messagesList)
            {
                // 用户和系统消息总是独立气泡
                if (msg.Role == "user" || msg.Role == "system")
                {
                    consolidatedList.Add(msg);
                    currentResponse = null;
                }
                else // assistant 或 tool
                {
                    if (currentResponse != null)
                    {
                        // 合并到当前响应气泡
                        if (!string.IsNullOrWhiteSpace(msg.Content))
                        {
                            if (!string.IsNullOrWhiteSpace(currentResponse.Content))
                            {
                                currentResponse.Content += "\n\n";
                            }
                            currentResponse.Content += msg.Content;
                        }
                        
                        // 合并附件
                        if (msg.Attachments != null && msg.Attachments.Count > 0)
                        {
                            currentResponse.Attachments.AddRange(msg.Attachments);
                        }
                        
                        // 更新时间戳为最新
                        currentResponse.Timestamp = msg.Timestamp;
                    }
                    else
                    {
                        // 开始新的响应气泡
                        // 如果是 tool 角色（且没有前置 assistant），强制转为 assistant 以便正确渲染气泡样式
                        if (msg.Role == "tool") 
                        {
                            msg.Role = "assistant";
                        }
                        
                        consolidatedList.Add(msg);
                        currentResponse = msg;
                    }
                }
            }

            return consolidatedList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for session {SessionId}", sessionId);
            return new List<MessageInfo>();
        }
    }

    public Task<MessageInfo> AddMessageAsync(string sessionId, string role, string content, List<AttachmentInfo>? attachments = null)
    {
        try
        {
            var now = DateTime.Now;
            var messageId = Guid.NewGuid().ToString("N");
            
            var message = new MessageInfo
            {
                Id = messageId,
                SessionId = sessionId,
                Role = role,
                Content = content,
                Timestamp = now,
                Attachments = attachments ?? new List<AttachmentInfo>()
            };

            _logger.LogInformation("Message created for session {SessionId}: {Role}", sessionId, role);
            return Task.FromResult(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding message for session {SessionId}", sessionId);
            throw;
        }
    }

    private string GenerateDefaultTitle(string sessionKey)
    {
        var sessionId = sessionKey.Replace("webui:", "");
        return $"会话 {sessionId.Substring(0, Math.Min(8, sessionId.Length))}";
    }

    private string? GetSnapshotUrl(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var normalized = imagePath.Replace('\\', '/');
        if (normalized.StartsWith("/api/files/sessions/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (Path.IsPathRooted(imagePath))
        {
            var sessionsRoot = _workspace.GetSessionsPath().Replace('\\', '/');
            if (!normalized.StartsWith(sessionsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return $"/api/files/local?path={Uri.EscapeDataString(imagePath)}";
            }

            normalized = normalized[sessionsRoot.Length..].TrimStart('/');
        }

        if (normalized.StartsWith("sessions/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["sessions/".Length..];
        }

        normalized = normalized.TrimStart('/');
        return string.IsNullOrWhiteSpace(normalized) ? null : $"/api/files/sessions/{normalized}";
    }

    private static bool TryGetJsonString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.GetRawText();
            return true;
        }

        return false;
    }

    private bool TryExtractSnapshotImageUrl(string toolContent, out string imageUrl)
    {
        imageUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(toolContent))
        {
            return false;
        }

        if (!TryParseToolResultJson(toolContent, out var rootElement))
        {
            return false;
        }

        if (!TryGetJsonString(rootElement, "action", out var action) ||
            string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        if (!string.Equals(action, "snapshot", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action, "capture", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryGetJsonString(rootElement, "imagePath", out var imagePath) ||
            string.IsNullOrWhiteSpace(imagePath))
        {
            return false;
        }

        var resolved = GetSnapshotUrl(imagePath);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        imageUrl = resolved;
        return true;
    }

    private static bool TryParseToolResultJson(string raw, out JsonElement rootElement)
    {
        rootElement = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim();
        if (!TryParseJsonWithRepair(normalized, out var firstPass))
        {
            return false;
        }

        if (firstPass.ValueKind == JsonValueKind.String)
        {
            var inner = firstPass.GetString();
            if (string.IsNullOrWhiteSpace(inner))
            {
                return false;
            }

            return TryParseJsonWithRepair(inner, out rootElement) && rootElement.ValueKind == JsonValueKind.Object;
        }

        if (firstPass.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        rootElement = firstPass;
        return true;
    }

    private static bool TryParseJsonWithRepair(string raw, out JsonElement rootElement)
    {
        rootElement = default;
        var normalized = raw.Trim();

        try
        {
            rootElement = JsonSerializer.Deserialize<JsonElement>(normalized);
            return true;
        }
        catch (JsonException)
        {
            if (!normalized.Contains("\\u0022", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                rootElement = JsonSerializer.Deserialize<JsonElement>(normalized.Replace("\\u0022", "\""));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static string WrapToolHintAsHtml(string toolHint)
    {
        var normalized = toolHint.Trim();
        var encoded = WebUtility.HtmlEncode(normalized);
        return $"<div class=\"nb-tool-hint\">{encoded}</div>";
    }

    private static bool TryExtractSessionImages(JsonElement message, out List<SessionImageItem> images)
    {
        images = [];
        if (!message.TryGetProperty("images", out var imagesElement) || imagesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var image in imagesElement.EnumerateArray())
        {
            var originalUrl = image.TryGetProperty("original_url", out var originalElement)
                ? originalElement.GetString() ?? string.Empty
                : string.Empty;
            var thumbnailUrl = image.TryGetProperty("thumbnail_url", out var thumbnailElement)
                ? thumbnailElement.GetString() ?? string.Empty
                : string.Empty;
            var summary = image.TryGetProperty("summary", out var summaryElement)
                ? summaryElement.GetString() ?? string.Empty
                : string.Empty;
            var width = image.TryGetProperty("width", out var widthElement) && widthElement.ValueKind == JsonValueKind.Number
                ? widthElement.GetInt32()
                : 0;
            var height = image.TryGetProperty("height", out var heightElement) && heightElement.ValueKind == JsonValueKind.Number
                ? heightElement.GetInt32()
                : 0;
            var contentType = image.TryGetProperty("content_type", out var contentTypeElement)
                ? contentTypeElement.GetString() ?? string.Empty
                : string.Empty;
            var fileSize = image.TryGetProperty("file_size", out var fileSizeElement) && fileSizeElement.ValueKind == JsonValueKind.Number
                ? fileSizeElement.GetInt64()
                : 0;

            if (string.IsNullOrWhiteSpace(originalUrl) || string.IsNullOrWhiteSpace(thumbnailUrl))
            {
                continue;
            }

            images.Add(new SessionImageItem(
                OriginalUrl: originalUrl,
                ThumbnailUrl: thumbnailUrl,
                Summary: summary,
                Width: width,
                Height: height,
                ContentType: contentType,
                FileSize: fileSize));
        }

        return images.Count > 0;
    }

    private static string AppendImageSummaries(string content, List<SessionImageItem> images)
    {
        if (images.Count == 0)
        {
            return content;
        }

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
}
