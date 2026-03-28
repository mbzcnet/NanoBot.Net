using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Agent.Extensions;
using NanoBot.Core.Workspace;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace NanoBot.Agent;

public interface ISessionManager
{
    Task<AgentSession> GetOrCreateSessionAsync(string sessionKey, CancellationToken cancellationToken = default);
    Task SaveSessionAsync(AgentSession session, string sessionKey, CancellationToken cancellationToken = default);
    Task ClearSessionAsync(string sessionKey, CancellationToken cancellationToken = default);
    Task InvalidateAsync(string sessionKey);
    IEnumerable<SessionFileInfo> ListSessions();

    int GetLastConsolidated(string sessionKey);

    void SetLastConsolidated(string sessionKey, int lastConsolidated);

    string? GetSessionTitle(string sessionKey);
    void SetSessionTitle(string sessionKey, string title);
    string? GetSessionProfileId(string sessionKey);
    void SetSessionProfileId(string sessionKey, string? profileId);
}

public record SessionFileInfo(
    string Key,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    string Path,
    string? Title = null,
    string? ProfileId = null
);

public sealed class SessionManager : ISessionManager
{
    private static readonly Regex MarkdownImageRegex = new(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)(?:\s+""(?<title>[^""]*)"")?\)", RegexOptions.Compiled);
    private readonly ChatClientAgent _agent;
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<SessionManager>? _logger;
    private readonly Dictionary<string, AgentSession> _cache;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _sessionsDirectory;

    private readonly string _legacySessionsDirectory;

    private readonly Dictionary<string, int> _lastConsolidatedBySessionKey;

    private sealed record SessionImageMetadata(
        string OriginalUrl,
        string ThumbnailUrl,
        string Summary,
        int Width,
        int Height,
        string ContentType,
        long FileSize);

    public SessionManager(
        ChatClientAgent agent,
        IWorkspaceManager workspace,
        ILogger<SessionManager>? logger = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger;
        _cache = new Dictionary<string, AgentSession>();
        _lastConsolidatedBySessionKey = new Dictionary<string, int>(StringComparer.Ordinal);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        _sessionsDirectory = _workspace.GetSessionsPath();

        _legacySessionsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nanobot",
            "sessions");

        if (!Directory.Exists(_sessionsDirectory))
        {
            Directory.CreateDirectory(_sessionsDirectory);
        }
    }

    public async Task<AgentSession> GetOrCreateSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(sessionKey, out var cachedSession))
        {
            return cachedSession;
        }

        var session = await LoadSessionAsync(sessionKey, cancellationToken);
        if (session != null)
        {
            _cache[sessionKey] = session;
            return session;
        }

        var newSession = await _agent.CreateSessionAsync(cancellationToken);
        _cache[sessionKey] = newSession;

        _logger?.LogDebug("Created new session for key {SessionKey}", sessionKey);

        return newSession;
    }

    public int GetLastConsolidated(string sessionKey)
    {
        return _lastConsolidatedBySessionKey.GetValueOrDefault(sessionKey, 0);
    }

    public void SetLastConsolidated(string sessionKey, int lastConsolidated)
    {
        if (lastConsolidated < 0)
        {
            lastConsolidated = 0;
        }

        _lastConsolidatedBySessionKey[sessionKey] = lastConsolidated;
    }

    public async Task SaveSessionAsync(AgentSession session, string sessionKey, CancellationToken cancellationToken = default)
    {
        var sessionFile = GetSessionPath(sessionKey);

        try
        {
            var allMessages = GetAllMessages(session);

            var sessionJson = await _agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
            var metadata = await BuildMetadataLineAsync(sessionKey, sessionFile, sessionJson, cancellationToken);

            await using var fs = new FileStream(sessionFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(fs);

            await writer.WriteLineAsync(metadata);

            foreach (var m in allMessages)
            {
                await writer.WriteLineAsync(m.ToJsonString(_jsonOptions));
            }

            _cache[sessionKey] = session;

            _logger?.LogDebug("Saved session {SessionKey} to {SessionFile}", sessionKey, sessionFile);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save session {SessionKey}", sessionKey);
            throw;
        }
    }

    public Task ClearSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var sessionFile = GetSessionPath(sessionKey);

        if (File.Exists(sessionFile))
        {
            File.Delete(sessionFile);
            _logger?.LogDebug("Deleted session file {SessionFile}", sessionFile);
        }

        _cache.Remove(sessionKey);
        _logger?.LogInformation("Cleared session {SessionKey}", sessionKey);

        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string sessionKey)
    {
        _cache.Remove(sessionKey);
        _logger?.LogDebug("Invalidated session cache for {SessionKey}", sessionKey);
        return Task.CompletedTask;
    }

    public IEnumerable<SessionFileInfo> ListSessions()
    {
        if (!Directory.Exists(_sessionsDirectory))
        {
            return [];
        }

        var sessions = new List<SessionFileInfo>();

        foreach (var file in Directory.GetFiles(_sessionsDirectory, "*.jsonl"))
        {
            try
            {
                var key = Path.GetFileNameWithoutExtension(file).Replace("_", ":");
                var info = new FileInfo(file);
                var (title, profileId) = LoadSessionMetadata(file);
                sessions.Add(new SessionFileInfo(
                    Key: key,
                    CreatedAt: info.CreationTimeUtc,
                    UpdatedAt: info.LastWriteTimeUtc,
                    Path: file,
                    Title: title,
                    ProfileId: profileId
                ));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read session file {File}", file);
            }
        }

        return sessions.OrderByDescending(s => s.UpdatedAt);
    }

    private readonly Dictionary<string, string> _sessionTitles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _sessionProfileIds = new(StringComparer.Ordinal);

    public string? GetSessionTitle(string sessionKey)
    {
        return _sessionTitles.GetValueOrDefault(sessionKey);
    }

    public void SetSessionTitle(string sessionKey, string title)
    {
        _sessionTitles[sessionKey] = title;
    }

    public string? GetSessionProfileId(string sessionKey)
    {
        return _sessionProfileIds.GetValueOrDefault(sessionKey);
    }

    public void SetSessionProfileId(string sessionKey, string? profileId)
    {
        _sessionProfileIds[sessionKey] = profileId;
    }

    private (string? title, string? profileId) LoadSessionMetadata(string sessionFile)
    {
        try
        {
            if (!File.Exists(sessionFile))
                return (null, null);

            using var fs = new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            var firstLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(firstLine))
                return (null, null);

            var metadata = JsonSerializer.Deserialize<JsonObject>(firstLine, _jsonOptions);
            if (metadata == null)
                return (null, null);

            var title = metadata.TryGetPropertyValue("title", out var titleNode) ? titleNode?.GetValue<string>() : null;
            var profileId = metadata.TryGetPropertyValue("profile_id", out var profileNode) ? profileNode?.GetValue<string>() : null;

            return (title, profileId);
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task<AgentSession?> LoadSessionAsync(string sessionKey, CancellationToken cancellationToken)
    {
        var sessionFile = GetSessionPath(sessionKey);

        await TryMigrateLegacySessionAsync(sessionKey, sessionFile, cancellationToken);

        if (!File.Exists(sessionFile))
        {
            return null;
        }

        try
        {
            await using var fs = new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);

            var firstLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return null;
            }

            JsonObject? metadataLine;
            try
            {
                metadataLine = JsonSerializer.Deserialize<JsonObject>(firstLine, _jsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }

            if (metadataLine == null)
            {
                return null;
            }

            if (metadataLine.TryGetPropertyValue("metadata", out var metaNode) && metaNode is JsonObject metaObj &&
                metaObj.TryGetPropertyValue("agent_session", out var agentSessionNode) && agentSessionNode != null)
            {
                if (metaObj.TryGetPropertyValue("last_consolidated", out var lcNode) && lcNode != null)
                {
                    if (lcNode.GetValueKind() == JsonValueKind.Number && lcNode.AsValue().TryGetValue<int>(out var lc))
                    {
                        SetLastConsolidated(sessionKey, lc);
                    }
                }

                // Restore profile_id from metadata
                if (metadataLine.TryGetPropertyValue("profile_id", out var profileNode) && profileNode != null)
                {
                    var profileId = profileNode.GetValue<string?>();
                    if (!string.IsNullOrEmpty(profileId))
                    {
                        SetSessionProfileId(sessionKey, profileId);
                        _logger?.LogDebug("Restored profile_id {ProfileId} for session {SessionKey}", profileId, sessionKey);
                    }
                }

                // Restore title from metadata
                if (metadataLine.TryGetPropertyValue("title", out var titleNode) && titleNode != null)
                {
                    var title = titleNode.GetValue<string?>();
                    if (!string.IsNullOrEmpty(title))
                    {
                        SetSessionTitle(sessionKey, title);
                        _logger?.LogDebug("Restored title {Title} for session {SessionKey}", title, sessionKey);
                    }
                }

                var raw = agentSessionNode.ToJsonString(_jsonOptions);
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(raw, _jsonOptions);
                var session = await _agent.DeserializeSessionAsync(jsonElement, cancellationToken: cancellationToken);

                _logger?.LogDebug("Loaded session {SessionKey} from {SessionFile}", sessionKey, sessionFile);
                return session;
            }

            var restoredSession = await RestoreSessionFromJsonlMessagesAsync(sessionFile, cancellationToken);
            _logger?.LogDebug("Restored session {SessionKey} from JSONL messages in {SessionFile}", sessionKey, sessionFile);
            return restoredSession;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load session {SessionKey}, creating new one", sessionKey);
            return null;
        }
    }

    private string GetSessionPath(string sessionKey)
    {
        var safeKey = sessionKey.Replace(":", "_").Replace("/", "_").Replace("\\", "_");
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safeKey = safeKey.Replace(c, '_');
        }
        return Path.Combine(_sessionsDirectory, $"{safeKey}.jsonl");
    }

    private async Task TryMigrateLegacySessionAsync(string sessionKey, string sessionFile, CancellationToken cancellationToken)
    {
        if (File.Exists(sessionFile))
        {
            return;
        }

        var legacyPath = GetLegacySessionPath(sessionKey);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(sessionFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.Move(legacyPath, sessionFile);
            _logger?.LogInformation("Migrated session {SessionKey} from legacy path {LegacyPath}", sessionKey, legacyPath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to migrate legacy session {SessionKey} from {LegacyPath}", sessionKey, legacyPath);
        }

        await Task.CompletedTask;
    }

    private sealed record JsonlSessionMessage(
        string Role,
        string Content,
        string Timestamp,
        JsonArray? ToolCalls = null,
        string? ToolCallId = null,
        string? Name = null);

    private List<JsonObject> GetAllMessages(AgentSession session)
    {
        var messages = session.GetAllMessages();
        return messages.Select(SerializeMessage).ToList();
    }

    private async Task<string> BuildMetadataLineAsync(
        string sessionKey,
        string sessionFile,
        JsonElement sessionJson,
        CancellationToken cancellationToken)
    {
        var createdAt = DateTimeOffset.Now;
        string? existingTitle = null;
        string? existingProfileId = null;

        if (File.Exists(sessionFile))
        {
            try
            {
                await using var fs = new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                var firstLine = await reader.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(firstLine))
                {
                    var existing = JsonSerializer.Deserialize<JsonObject>(firstLine, _jsonOptions);
                    if (existing != null)
                    {
                        if (existing.TryGetPropertyValue("created_at", out var createdNode) &&
                            createdNode != null &&
                            DateTimeOffset.TryParse(createdNode.GetValue<string>(), out var parsed))
                        {
                            createdAt = parsed;
                        }
                        if (existing.TryGetPropertyValue("title", out var titleNode))
                        {
                            existingTitle = titleNode?.GetValue<string>();
                        }
                        if (existing.TryGetPropertyValue("profile_id", out var profileNode))
                        {
                            existingProfileId = profileNode?.GetValue<string>();
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        var lastConsolidated = GetLastConsolidated(sessionKey);
        var title = GetSessionTitle(sessionKey) ?? existingTitle;
        var profileId = GetSessionProfileId(sessionKey) ?? existingProfileId;

        var metadataLine = new JsonObject
        {
            ["_type"] = "metadata",
            ["key"] = sessionKey,
            ["created_at"] = createdAt.ToString("o"),
            ["updated_at"] = DateTimeOffset.Now.ToString("o"),
            ["title"] = title,
            ["profile_id"] = profileId,
            ["last_consolidated"] = lastConsolidated,
            ["metadata"] = new JsonObject
            {
                ["last_consolidated"] = lastConsolidated,
                ["agent_session"] = JsonNode.Parse(sessionJson.GetRawText())
            }
        };

        return metadataLine.ToJsonString(_jsonOptions);
    }

    private async Task<AgentSession> RestoreSessionFromJsonlMessagesAsync(string sessionFile, CancellationToken cancellationToken)
    {
        var session = await _agent.CreateSessionAsync(cancellationToken);
        var messages = new List<ChatMessage>();

        await using var fs = new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);

        _ = await reader.ReadLineAsync(cancellationToken);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonObject? node;
            try
            {
                node = JsonSerializer.Deserialize<JsonObject>(line, _jsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (node == null)
            {
                continue;
            }

            var message = DeserializeMessage(node);
            if (message != null)
            {
                messages.Add(message);
            }
        }

        session.StateBag.SetValue("ChatHistoryProvider", messages);
        return session;
    }

    private ChatMessage? DeserializeMessage(JsonObject node)
    {
        var role = node.TryGetPropertyValue("role", out var roleNode)
            ? roleNode?.GetValue<string>()
            : null;
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        var content = node.TryGetPropertyValue("content", out var contentNode)
            ? contentNode?.GetValue<string>() ?? string.Empty
            : string.Empty;

        var chatRole = role.ToLowerInvariant() switch
        {
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            "system" => ChatRole.System,
            _ => ChatRole.User
        };

        var message = new ChatMessage(chatRole, content);

        if (node.TryGetPropertyValue("tool_calls", out var toolCallsNode) && toolCallsNode is JsonArray toolCallsArray)
        {
            foreach (var toolCallNode in toolCallsArray)
            {
                if (toolCallNode is not JsonObject toolCallObj)
                {
                    continue;
                }

                var callId = toolCallObj.TryGetPropertyValue("id", out var idNode) ? idNode?.GetValue<string>() : null;
                var functionObj = toolCallObj.TryGetPropertyValue("function", out var functionNode) ? functionNode as JsonObject : null;
                var name = functionObj?.TryGetPropertyValue("name", out var nameNode) == true ? nameNode?.GetValue<string>() : null;
                var argumentsJson = functionObj?.TryGetPropertyValue("arguments", out var argsNode) == true ? argsNode?.GetValue<string>() : null;

                if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                Dictionary<string, object?>? arguments = null;
                if (!string.IsNullOrWhiteSpace(argumentsJson))
                {
                    try
                    {
                        arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson, _jsonOptions);
                    }
                    catch (JsonException)
                    {
                        arguments = null;
                    }
                }

                message.Contents.Add(new FunctionCallContent(callId, name, arguments));
            }
        }

        if (chatRole == ChatRole.Tool &&
            node.TryGetPropertyValue("tool_call_id", out var toolCallIdNode) &&
            !string.IsNullOrWhiteSpace(toolCallIdNode?.GetValue<string>()))
        {
            message.Contents.Add(new FunctionResultContent(toolCallIdNode!.GetValue<string>()!, content));
        }

        return message;
    }

    private string GetLegacySessionPath(string sessionKey)
    {
        var safeKey = sessionKey.Replace(":", "_").Replace("/", "_").Replace("\\", "_");
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safeKey = safeKey.Replace(c, '_');
        }
        return Path.Combine(_legacySessionsDirectory, $"{safeKey}.jsonl");
    }

    private JsonObject SerializeMessage(ChatMessage message)
    {
        var timestamp = DateTimeOffset.Now.ToString("o");
        var role = message.Role.ToString().ToLowerInvariant();
        var content = message.Text ?? string.Empty;

        // Remove [TOOL_CALL] markers from content before saving
        content = RemoveToolCallMarkers(content);

        JsonArray? toolCalls = null;
        string? toolCallId = null;
        string? name = null;
        bool hasToolCalls = false;

        foreach (var c in message.Contents)
        {
            if (c is FunctionCallContent fcc)
            {
                hasToolCalls = true;
                toolCalls ??= new JsonArray();

                var argsDict = fcc.Arguments as IDictionary<string, object?>;
                var argsJson = argsDict != null ? JsonSerializer.Serialize(argsDict, _jsonOptions) : "{}";

                var functionObj = new JsonObject
                {
                    ["name"] = fcc.Name,
                    // arguments 需要可 grep 且与原项目一致地保持 JSON 字符串
                    ["arguments"] = argsJson
                };

                var toolCallObj = new JsonObject
                {
                    ["id"] = fcc.CallId,
                    ["type"] = "function",
                    ["function"] = functionObj
                };

                toolCalls.Add(toolCallObj);

                // 与 OpenAI 格式/原项目兼容：tool call 本身也可能需要 name
                name ??= fcc.Name;
            }
            else if (c is FunctionResultContent frc)
            {
                toolCallId ??= frc.CallId;
                
                // Ensure tool result is saved in content
                if (string.IsNullOrEmpty(content))
                {
                    if (frc.Result is string strResult)
                    {
                        content = strResult;
                    }
                    else if (frc.Result != null)
                    {
                        try
                        {
                            content = JsonSerializer.Serialize(frc.Result, _jsonOptions);
                        }
                        catch
                        {
                            content = frc.Result.ToString() ?? string.Empty;
                        }
                    }
                }
            }
        }
        
        // 保留原始消息内容，不做任何转换
        // 注意：tool_calls 的信息已经保存在上面的 toolCalls JSON 数组中
        // content 字段用于纯文本展示，但不应影响工具调用的实际功能
        
        content ??= string.Empty;

        // 分离文本内容和图片元数据存储
        // content 保持原始文本不变，图片信息存储到独立的 sessionImages 列表
        List<SessionImageMetadata>? sessionImages = null;
        if (!string.IsNullOrWhiteSpace(content))
        {
            sessionImages = ExtractImageMetadata(content);
        }

        var obj = new JsonObject
        {
            ["role"] = role,
            ["content"] = content,
            ["timestamp"] = timestamp
        };

        if (toolCalls != null)
        {
            obj["tool_calls"] = toolCalls;
        }

        if (!string.IsNullOrEmpty(toolCallId))
        {
            obj["tool_call_id"] = toolCallId;
        }

        if (!string.IsNullOrEmpty(name))
        {
            obj["name"] = name;
        }

        if (sessionImages is { Count: > 0 })
        {
            var imagesArray = new JsonArray();
            foreach (var image in sessionImages)
            {
                imagesArray.Add(new JsonObject
                {
                    ["original_url"] = image.OriginalUrl,
                    ["thumbnail_url"] = image.ThumbnailUrl,
                    ["summary"] = image.Summary,
                    ["width"] = image.Width,
                    ["height"] = image.Height,
                    ["content_type"] = image.ContentType,
                    ["file_size"] = image.FileSize
                });
            }

            obj["images"] = imagesArray;
        }

        return obj;
    }

    private string BuildHistoryImageContent(string content, out List<SessionImageMetadata>? metadataList)
    {
        metadataList = null;
        var matches = MarkdownImageRegex.Matches(content);
        if (matches.Count == 0)
        {
            return content;
        }

        var updatedContent = content;
        var images = new List<SessionImageMetadata>();
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var fullMatch = match.Value;
            var alt = match.Groups["alt"].Value;
            var title = match.Groups["title"].Value;
            var url = match.Groups["url"].Value.Trim();
            if (!TryCreateThumbnail(url, alt, title, out var metadata))
            {
                continue;
            }

            var linkText = string.IsNullOrWhiteSpace(alt) ? "图片" : alt;
            var replacement = $"[![{linkText}]({metadata.ThumbnailUrl})]({metadata.OriginalUrl})";
            updatedContent = updatedContent.Replace(fullMatch, replacement, StringComparison.Ordinal);
            images.Add(metadata);
        }

        metadataList = images.Count == 0 ? null : images;
        return updatedContent;
    }

    /// <summary>
    /// 从内容中提取图片元数据，不修改原始文本内容
    /// </summary>
    private List<SessionImageMetadata>? ExtractImageMetadata(string content)
    {
        var matches = MarkdownImageRegex.Matches(content);
        if (matches.Count == 0)
        {
            return null;
        }

        var images = new List<SessionImageMetadata>();
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var alt = match.Groups["alt"].Value;
            var title = match.Groups["title"].Value;
            var url = match.Groups["url"].Value.Trim();
            if (TryCreateThumbnail(url, alt, title, out var metadata))
            {
                images.Add(metadata);
            }
        }

        return images.Count == 0 ? null : images;
    }

    private bool TryCreateThumbnail(string imageUrl, string alt, string title, out SessionImageMetadata metadata)
    {
        metadata = default!;
        if (!TryResolveSessionImagePath(imageUrl, out var fullPath, out var relativePath))
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        var sessionsRoot = _workspace.GetSessionsPath();
        var sessionId = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        try
        {
            using var image = Image.Load(fullPath);
            var width = image.Width;
            var height = image.Height;
            var maxEdge = Math.Max(width, height);
            var ratio = maxEdge <= 320 ? 1d : 320d / maxEdge;
            var newWidth = Math.Max(1, (int)Math.Round(width * ratio));
            var newHeight = Math.Max(1, (int)Math.Round(height * ratio));

            var thumbFileName = $"{Path.GetFileNameWithoutExtension(fullPath)}_thumb.jpg";
            var thumbRelative = Path.Combine(sessionId, "thumbnails", thumbFileName).Replace('\\', '/');
            var thumbFullPath = Path.Combine(sessionsRoot, thumbRelative.Replace('/', Path.DirectorySeparatorChar));

            var thumbDirectory = Path.GetDirectoryName(thumbFullPath);
            if (!string.IsNullOrWhiteSpace(thumbDirectory))
            {
                Directory.CreateDirectory(thumbDirectory);
            }

            if (!File.Exists(thumbFullPath))
            {
                image.Mutate(ctx => ctx.Resize(newWidth, newHeight));
                image.SaveAsJpeg(thumbFullPath);
            }

            var summaryText = !string.IsNullOrWhiteSpace(alt)
                ? alt
                : !string.IsNullOrWhiteSpace(title)
                    ? title
                    : $"图片 {Path.GetFileName(fullPath)}（{width}×{height}）";

            var fileSize = new FileInfo(fullPath).Length;
            metadata = new SessionImageMetadata(
                OriginalUrl: ToSessionFileUrl(relativePath),
                ThumbnailUrl: ToSessionFileUrl(thumbRelative),
                Summary: summaryText,
                Width: width,
                Height: height,
                ContentType: GetContentType(fullPath),
                FileSize: fileSize);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to create thumbnail for session image {ImageUrl}", imageUrl);
            return false;
        }
    }

    private bool TryResolveSessionImagePath(string imageUrl, out string fullPath, out string relativePath)
    {
        fullPath = string.Empty;
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return false;
        }

        var raw = imageUrl.Trim();
        if (raw.StartsWith("/api/files/sessions/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = raw["/api/files/sessions/".Length..].TrimStart('/');
            fullPath = Path.Combine(_workspace.GetSessionsPath(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return true;
        }

        if (Path.IsPathRooted(raw))
        {
            var sessionsRoot = _workspace.GetSessionsPath();
            if (!raw.StartsWith(sessionsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fullPath = raw;
            relativePath = Path.GetRelativePath(sessionsRoot, raw).Replace('\\', '/');
            return true;
        }

        return false;
    }

    private static string GetContentType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    private static string ToSessionFileUrl(string relativePath)
    {
        return $"/api/files/sessions/{relativePath.Replace('\\', '/')}";
    }

    /// <summary>
    /// 从 content 中移除 [TOOL_CALL] 标记
    /// 这些标记是展示用的，不应该保存到会话历史中
    /// </summary>
    private static string RemoveToolCallMarkers(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        // 移除 [TOOL_CALL]xxx[/TOOL_CALL] 格式
        var result = ToolCallMarkerRegex.Replace(content, "");

        // 清理多余的空白行
        result = MultiBlankLineRegex.Replace(result, "\n\n");

        return result.Trim();
    }

    private static readonly Regex ToolCallMarkerRegex = new(@"\[TOOL_CALL\].*?\[/TOOL_CALL\]", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex MultiBlankLineRegex = new(@"\n{3,}", RegexOptions.Compiled);
}
