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

            var lines = await File.ReadAllLinesAsync(sessionFile);
            var messagesList = new List<MessageInfo>();

            if (lines.Length > 0 &&
                TryReadMessagesFromMetadata(lines[0], sessionId, out var metadataMessages) &&
                metadataMessages.Count > 0)
            {
                messagesList = metadataMessages;
            }
            else
            {
                messagesList = ReadMessagesFromJsonLines(lines, sessionId);
            }

            return ConsolidateMessages(messagesList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for session {SessionId}", sessionId);
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

                ToolCallInfo? toolCallInfo = null;
                var toolExecutions = new List<ToolExecutionInfo>();
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

                    var firstToolCall = toolCalls.FirstOrDefault();
                    if (firstToolCall != null)
                    {
                        var argsJson = firstToolCall.Arguments != null
                            ? JsonSerializer.Serialize(firstToolCall.Arguments)
                            : "{}";
                        toolCallInfo = new ToolCallInfo(
                            firstToolCall.Name ?? string.Empty,
                            argsJson,
                            firstToolCall.CallId
                        );
                    }

                    foreach (var toolCall in toolCalls)
                    {
                        toolExecutions.Add(new ToolExecutionInfo
                        {
                            CallId = toolCall.CallId,
                            Name = toolCall.Name ?? string.Empty,
                            Arguments = toolCall.Arguments != null ? JsonSerializer.Serialize(toolCall.Arguments) : "{}"
                        });
                    }
                }

                if (role == "tool")
                {
                    var toolCallId = msg.TryGetProperty("tool_call_id", out var toolCallIdElement)
                        ? toolCallIdElement.GetString() ?? string.Empty
                        : string.Empty;
                    var toolName = msg.TryGetProperty("name", out var toolNameElement)
                        ? toolNameElement.GetString() ?? string.Empty
                        : string.Empty;

                    if (!string.IsNullOrWhiteSpace(content) || !string.IsNullOrWhiteSpace(toolCallId) || !string.IsNullOrWhiteSpace(toolName))
                    {
                        toolExecutions.Add(new ToolExecutionInfo
                        {
                            CallId = toolCallId,
                            Name = toolName,
                            Arguments = "{}",
                            Output = NormalizeToolOutput(content),
                            IsError = LooksLikeErrorOutput(content)
                        });
                    }
                }

                if (role == "tool" && !string.IsNullOrWhiteSpace(content))
                {
                    if (TryExtractSnapshotImageUrl(content, out var snapshotImageUrl))
                    {
                        content = $"![snapshot]({snapshotImageUrl})";
                    }

                    content = string.Empty;
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

                if (!string.IsNullOrWhiteSpace(content) || toolCallInfo != null || attachments.Count > 0 || toolExecutions.Count > 0)
                {
                    messagesList.Add(new MessageInfo
                    {
                        Id = $"{sessionId}_{messagesList.Count}",
                        SessionId = sessionId,
                        Role = role,
                        Content = content,
                        Timestamp = timestamp,
                        Attachments = attachments,
                        ToolCall = toolCallInfo,
                        ToolExecutions = toolExecutions,
                        SourceIndex = messagesList.Count
                    });
                }
            }
            catch
            {
            }
        }

        return messagesList;
    }

    private List<MessageInfo> ConsolidateMessages(List<MessageInfo> messagesList)
    {
        var consolidatedList = new List<MessageInfo>();
        MessageInfo? currentResponse = null;

        foreach (var msg in messagesList)
        {
            if (msg.Role == "user" || msg.Role == "system")
            {
                consolidatedList.Add(msg);
                currentResponse = null;
            }
            else
            {
                if (currentResponse != null)
                {
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                    {
                        if (!string.IsNullOrWhiteSpace(currentResponse.Content))
                        {
                            currentResponse.Content += "\n\n";
                        }
                        currentResponse.Content += msg.Content;
                    }

                    if (msg.Attachments != null && msg.Attachments.Count > 0)
                    {
                        currentResponse.Attachments.AddRange(msg.Attachments);
                    }

                    if (currentResponse.ToolCall == null && msg.ToolCall != null)
                    {
                        currentResponse.ToolCall = msg.ToolCall;
                    }

                    if (msg.ToolExecutions.Count > 0)
                    {
                        MergeToolExecutions(currentResponse.ToolExecutions, msg.ToolExecutions);
                    }

                    currentResponse.Timestamp = msg.Timestamp;
                    currentResponse.SourceIndex = Math.Max(currentResponse.SourceIndex, msg.SourceIndex);
                }
                else
                {
                    if (msg.Role == "tool")
                    {
                        msg.Role = "assistant";
                    }

                    var retryCandidate = consolidatedList.LastOrDefault(m => m.Role == "user");
                    msg.RetryPrompt = retryCandidate?.Content;
                    msg.RetryFromIndex = retryCandidate?.SourceIndex;
                    consolidatedList.Add(msg);
                    currentResponse = msg;
                }
            }
        }

        return consolidatedList;
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

            if (!string.IsNullOrWhiteSpace(incoming.CallId) &&
                string.IsNullOrWhiteSpace(existing.CallId))
            {
                existing.CallId = incoming.CallId;
            }

            if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(incoming.Name))
            {
                existing.Name = incoming.Name;
            }

            if ((string.IsNullOrWhiteSpace(existing.Arguments) || existing.Arguments == "{}") &&
                !string.IsNullOrWhiteSpace(incoming.Arguments) &&
                incoming.Arguments != "{}")
            {
                existing.Arguments = incoming.Arguments;
            }

            if (string.IsNullOrWhiteSpace(existing.Output) && !string.IsNullOrWhiteSpace(incoming.Output))
            {
                existing.Output = incoming.Output;
            }
            else if (!string.IsNullOrWhiteSpace(incoming.Output) &&
                     !string.Equals(existing.Output, incoming.Output, StringComparison.Ordinal))
            {
                existing.Output = string.IsNullOrWhiteSpace(existing.Output)
                    ? incoming.Output
                    : $"{existing.Output}\n{incoming.Output}";
            }

            existing.IsError = existing.IsError || incoming.IsError || LooksLikeErrorOutput(existing.Output);
        }
    }

    private static bool LooksLikeErrorOutput(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("error", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("ERR_", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("SSL", StringComparison.OrdinalIgnoreCase);
    }

    private string NormalizeToolOutput(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return string.Empty;
        }

        var normalized = rawOutput.Trim();
        if (TryExtractSnapshotMarkdown(normalized, out var snapshotMarkdown))
        {
            return string.IsNullOrWhiteSpace(snapshotMarkdown)
                ? normalized
                : $"{normalized}\n\n{snapshotMarkdown}";
        }

        return normalized;
    }

    private bool TryReadMessagesFromMetadata(string metadataLine, string sessionId, out List<MessageInfo> messages)
    {
        messages = new List<MessageInfo>();

        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(metadataLine);
            if (!root.TryGetProperty("metadata", out var metadata) ||
                !metadata.TryGetProperty("agent_session", out var agentSession) ||
                !agentSession.TryGetProperty("stateBag", out var stateBag) ||
                !stateBag.TryGetProperty("FileBackedChatHistoryProvider", out var provider) ||
                provider.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var index = 0;
            foreach (var item in provider.EnumerateArray())
            {
                var role = item.TryGetProperty("role", out var roleEl)
                    ? roleEl.GetString()?.ToLowerInvariant() ?? "user"
                    : "user";

                var timestamp = DateTime.Now;
                if (item.TryGetProperty("createdAt", out var createdAtEl) &&
                    createdAtEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(createdAtEl.GetString(), out var createdAt))
                {
                    timestamp = createdAt;
                }

                var textParts = new List<string>();
                ToolCallInfo? toolCall = null;
                var toolExecutions = new List<ToolExecutionInfo>();
                var toolExecutionLookup = new Dictionary<string, ToolExecutionInfo>(StringComparer.Ordinal);
                var itemToolCallId = item.TryGetProperty("toolCallId", out var itemToolCallIdEl)
                    ? itemToolCallIdEl.GetString() ?? string.Empty
                    : item.TryGetProperty("tool_call_id", out var itemToolCallIdAltEl)
                        ? itemToolCallIdAltEl.GetString() ?? string.Empty
                        : string.Empty;
                var itemToolName = item.TryGetProperty("name", out var itemToolNameEl)
                    ? itemToolNameEl.GetString() ?? string.Empty
                    : string.Empty;

                if (item.TryGetProperty("content", out var itemContentEl))
                {
                    if (itemContentEl.ValueKind == JsonValueKind.String)
                    {
                        var text = itemContentEl.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            textParts.Add(text!);
                        }
                    }
                    else if (itemContentEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var part in itemContentEl.EnumerateArray())
                        {
                            if (part.ValueKind == JsonValueKind.String)
                            {
                                var text = part.GetString();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    textParts.Add(text!);
                                }
                            }
                            else if (part.TryGetProperty("text", out var partTextEl) && partTextEl.ValueKind == JsonValueKind.String)
                            {
                                var text = partTextEl.GetString();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    textParts.Add(text!);
                                }
                            }
                        }
                    }
                }

                if (item.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array)
                {
                    foreach (var content in contents.EnumerateArray())
                    {
                        if (!content.TryGetProperty("$type", out var typeEl))
                        {
                            continue;
                        }

                        var type = typeEl.GetString();
                        if (type == "text" &&
                            content.TryGetProperty("text", out var textEl) &&
                            textEl.ValueKind == JsonValueKind.String)
                        {
                            var text = textEl.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                textParts.Add(text!);
                            }
                        }
                        else if (type == "functionCall")
                        {
                            var name = content.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                            var callId = content.TryGetProperty("callId", out var callIdEl) ? callIdEl.GetString() : null;
                            string args = "{}";
                            if (content.TryGetProperty("arguments", out var argsEl))
                            {
                                args = argsEl.GetRawText();
                            }

                            toolCall = new ToolCallInfo(name, args, callId);
                            var execution = new ToolExecutionInfo
                            {
                                CallId = callId ?? string.Empty,
                                Name = name,
                                Arguments = args
                            };
                            toolExecutions.Add(execution);
                            if (!string.IsNullOrWhiteSpace(execution.CallId))
                            {
                                toolExecutionLookup[execution.CallId] = execution;
                            }
                        }
                        else if (type == "functionResult" &&
                                 content.TryGetProperty("result", out var resultEl))
                        {
                            var result = resultEl.ValueKind == JsonValueKind.String
                                ? resultEl.GetString()
                                : resultEl.GetRawText();

                            if (!string.IsNullOrWhiteSpace(result))
                            {
                                var callId = content.TryGetProperty("callId", out var resultCallIdEl)
                                    ? resultCallIdEl.GetString() ?? string.Empty
                                    : string.Empty;
                                if (!string.IsNullOrWhiteSpace(callId) && toolExecutionLookup.TryGetValue(callId, out var existingExecution))
                                {
                                    existingExecution.Output = NormalizeToolOutput(result!);
                                    existingExecution.IsError = LooksLikeErrorOutput(result!);
                                }
                                else
                                {
                                    toolExecutions.Add(new ToolExecutionInfo
                                    {
                                        CallId = callId,
                                        Name = "tool",
                                        Arguments = "{}",
                                        Output = NormalizeToolOutput(result!),
                                        IsError = LooksLikeErrorOutput(result!)
                                    });
                                }
                            }
                        }
                    }
                }

                var combinedContent = string.Join("\n\n", textParts.Where(p => !string.IsNullOrWhiteSpace(p)));

                if (role == "tool")
                {
                    if (!string.IsNullOrWhiteSpace(combinedContent) ||
                        !string.IsNullOrWhiteSpace(itemToolCallId) ||
                        !string.IsNullOrWhiteSpace(itemToolName))
                    {
                        toolExecutions.Add(new ToolExecutionInfo
                        {
                            CallId = itemToolCallId,
                            Name = itemToolName,
                            Arguments = "{}",
                            Output = NormalizeToolOutput(combinedContent),
                            IsError = LooksLikeErrorOutput(combinedContent)
                        });
                    }
                    combinedContent = string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(combinedContent) || toolCall != null || toolExecutions.Count > 0)
                {
                    messages.Add(new MessageInfo
                    {
                        Id = item.TryGetProperty("messageId", out var messageIdEl) ? messageIdEl.GetString() ?? $"{sessionId}_{index}" : $"{sessionId}_{index}",
                        SessionId = sessionId,
                        Role = role,
                        Content = combinedContent,
                        Timestamp = timestamp,
                        ToolCall = toolCall,
                        ToolExecutions = toolExecutions,
                        SourceIndex = index
                    });
                }

                index++;
            }

            return messages.Count > 0;
        }
        catch
        {
            messages = new List<MessageInfo>();
            return false;
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

    public async Task DeleteMessagesFromAsync(string sessionId, int fromIndex)
    {
        try
        {
            var sessionKey = $"webui:{sessionId}";
            var sessionsPath = _workspace.GetSessionsPath();
            var sessionFile = Path.Combine(sessionsPath, $"{sessionKey.Replace(":", "_")}.jsonl");

            if (!File.Exists(sessionFile))
            {
                _logger.LogWarning("Session file not found for {SessionId}", sessionId);
                return;
            }

            // 读取所有行
            var allLines = await File.ReadAllLinesAsync(sessionFile);
            if (allLines.Length == 0)
            {
                return;
            }

            // 第一行是 metadata，需要保留
            var metadataLine = allLines[0];
            var messageLines = allLines.Skip(1).ToList();

            // 过滤掉 metadata 行（_type 为 metadata 的行）
            var actualMessageLines = messageLines.Where(line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return false;
                try
                {
                    var doc = JsonSerializer.Deserialize<JsonElement>(line);
                    if (doc.TryGetProperty("_type", out var typeElement))
                    {
                        return typeElement.GetString() != "metadata";
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }).ToList();

            // 验证索引范围
            if (fromIndex < 0 || fromIndex >= actualMessageLines.Count)
            {
                _logger.LogWarning("Invalid message index {FromIndex} for session {SessionId}, total messages: {Count}", 
                    fromIndex, sessionId, actualMessageLines.Count);
                return;
            }

            // 保留从开头到 fromIndex 之前的消息
            var linesToKeep = actualMessageLines.Take(fromIndex).ToList();

            // 重新构建文件内容
            var newLines = new List<string> { metadataLine };
            newLines.AddRange(linesToKeep);

            // 写回文件
            await File.WriteAllLinesAsync(sessionFile, newLines);

            // 清除会话缓存，强制重新加载
            await _sessionManager.InvalidateAsync(sessionKey);

            _logger.LogInformation("Deleted messages from index {FromIndex} for session {SessionId}, remaining messages: {Count}", 
                fromIndex, sessionId, linesToKeep.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting messages from index {FromIndex} for session {SessionId}", fromIndex, sessionId);
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

    private bool TryExtractSnapshotMarkdown(string toolContent, out string markdown)
    {
        markdown = string.Empty;
        if (!TryExtractSnapshotImageUrl(toolContent, out var imageUrl) ||
            string.IsNullOrWhiteSpace(imageUrl))
        {
            return false;
        }

        markdown = $"![snapshot]({imageUrl})";
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
