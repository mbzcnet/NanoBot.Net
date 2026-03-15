using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Agent.Extensions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;
using NanoBot.Core.Debug;
using NanoBot.Core.Memory;
using NanoBot.Core.Skills;
using NanoBot.Core.Subagents;
using NanoBot.Core.Tools;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Memory;
using NanoBot.Providers;

namespace NanoBot.Agent;

public interface IAgentRuntime
{
    Task RunAsync(CancellationToken cancellationToken = default);
    void Stop();
    Task<string> ProcessDirectAsync(string content, string sessionKey = "chat_direct", string channel = "cli", string chatId = "direct", CancellationToken cancellationToken = default);
    IAsyncEnumerable<AgentResponseUpdate> ProcessDirectStreamingAsync(string content, string sessionKey = "chat_direct", string channel = "cli", string chatId = "direct", CancellationToken cancellationToken = default);
    Task<bool> TryCancelSessionAsync(string sessionKey);
    void SetRuntimeMetadata(string sessionKey, IReadOnlyDictionary<string, string> metadata);
    void ClearAgentCache();
}

public sealed class AgentRuntime : IAgentRuntime, IDisposable
{
    private static readonly Regex MarkdownImageRegex = new(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.Compiled);
    private static readonly Regex DefaultSessionTitleRegex = new(@"^会话 \d{2}-\d{2} \d{2}:\d{2}$", RegexOptions.Compiled);
    private readonly ChatClientAgent _defaultAgent;
    private readonly IMessageBus _bus;
    private readonly ISessionManager _sessionManager;
    private readonly IWorkspaceManager _workspace;
    private readonly IMemoryStore? _memoryStore;
    private readonly ISubagentManager? _subagentManager;
    private readonly ILogger<AgentRuntime>? _logger;
    private readonly IDebugState? _debugState;
    private readonly string _sessionsDirectory;
    private readonly int _memoryWindow;
    private CancellationTokenSource? _runningCts;
    private bool _disposed;
    private bool _stopped;

    // Profile-aware chat client support
    private readonly IChatClientFactory? _chatClientFactory;
    private readonly LlmConfig? _llmConfig;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ConcurrentDictionary<string, ChatClientAgent> _profileAgents = new(StringComparer.Ordinal);

    // Command registry
    private readonly Dictionary<string, CommandDefinition> _commands = new(StringComparer.OrdinalIgnoreCase);

    // Session cancellation tokens
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessionTokens = new(StringComparer.Ordinal);

    // Runtime metadata
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _runtimeMetadata = new(StringComparer.Ordinal);

    private record CommandDefinition(
        string Name,
        string Description,
        bool Immediate,
        Func<InboundMessage, CancellationToken, Task<OutboundMessage?>> Handler
    );

    public AgentRuntime(
        ChatClientAgent agent,
        IMessageBus bus,
        ISessionManager sessionManager,
        IWorkspaceManager workspace,
        IMemoryStore? memoryStore,
        ISubagentManager? subagentManager,
        int memoryWindow,
        IChatClientFactory? chatClientFactory = null,
        LlmConfig? llmConfig = null,
        IServiceProvider? serviceProvider = null,
        ILogger<AgentRuntime>? logger = null,
        IDebugState? debugState = null)
    {
        _defaultAgent = agent ?? throw new ArgumentNullException(nameof(agent));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _memoryStore = memoryStore;
        _subagentManager = subagentManager;
        _memoryWindow = memoryWindow;
        _chatClientFactory = chatClientFactory;
        _llmConfig = llmConfig;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _debugState = debugState;
        _sessionsDirectory = _workspace.GetSessionsPath();

        if (!Directory.Exists(_sessionsDirectory))
        {
            Directory.CreateDirectory(_sessionsDirectory);
        }

        // Register built-in commands
        RegisterCommand(new CommandDefinition(
            Name: "/new",
            Description: "Start a new conversation",
            Immediate: false,
            Handler: async (msg, ct) =>
            {
                var existingSession = await _sessionManager.GetOrCreateSessionAsync(msg.SessionKey, ct);
                return await HandleNewSessionCommandAsync(msg, existingSession, ct);
            }
        ));

        RegisterCommand(new CommandDefinition(
            Name: "/help",
            Description: "Show available commands",
            Immediate: true,
            Handler: (msg, _) => Task.FromResult<OutboundMessage?>(new OutboundMessage
            {
                Channel = msg.Channel,
                ChatId = msg.ChatId,
                Content = BuildHelpText()
            })
        ));

        RegisterCommand(new CommandDefinition(
            Name: "/stop",
            Description: "Stop the current task",
            Immediate: true,
            Handler: async (msg, ct) =>
            {
                var sessionKey = msg.SessionKey;
                await TryCancelSessionAsync(sessionKey);
                return new OutboundMessage
                {
                    Channel = msg.Channel,
                    ChatId = msg.ChatId,
                    Content = "Task cancelled. Please resend your message if you want to continue."
                };
            }
        ));
    }

    private void RegisterCommand(CommandDefinition command)
    {
        _commands[command.Name] = command;
    }

    private string BuildHelpText()
    {
        var sb = new StringBuilder("🐈 nanobot commands:\n");
        foreach (var cmd in _commands.Values.OrderBy(c => c.Name))
        {
            sb.AppendLine($"{cmd.Name} — {cmd.Description}");
        }
        return sb.ToString().TrimEnd();
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _runningCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _stopped = false;

        _logger?.LogInformation("Agent runtime started");

        try
        {
            while (!_stopped && !_runningCts.Token.IsCancellationRequested)
            {
                try
                {
                    var msg = await _bus.ConsumeInboundAsync(_runningCts.Token);

                    try
                    {
                        var response = await ProcessMessageAsync(msg, _runningCts.Token);
                        if (response != null)
                        {
                            await _bus.PublishOutboundAsync(response, _runningCts.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing message from {Channel}:{ChatId}", msg.Channel, msg.ChatId);

                        await _bus.PublishOutboundAsync(new OutboundMessage
                        {
                            Channel = msg.Channel,
                            ChatId = msg.ChatId,
                            Content = $"Sorry, I encountered an error: {ex.Message}"
                        }, _runningCts.Token);
                    }
                }
                catch (OperationCanceledException) when (_stopped)
                {
                    break;
                }
            }
        }
        finally
        {
            _logger?.LogInformation("Agent runtime stopped");
        }
    }

    public void Stop()
    {
        if (_stopped) return;

        _stopped = true;
        _runningCts?.Cancel();
        _logger?.LogInformation("Agent runtime stopping");
    }

    public async Task<string> ProcessDirectAsync(
        string content,
        string sessionKey = "chat_direct",
        string channel = "cli",
        string chatId = "direct",
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var msg = new InboundMessage
        {
            Channel = channel,
            SenderId = "user",
            ChatId = chatId,
            Content = content
        };

        var response = await ProcessMessageAsync(msg, cancellationToken, sessionKey);
        sw.Stop();
        _logger?.LogInformation("[TIMING] ProcessDirectAsync total: {ElapsedMs}ms", sw.ElapsedMilliseconds);
        return response?.Content ?? string.Empty;
    }

    public async IAsyncEnumerable<AgentResponseUpdate> ProcessDirectStreamingAsync(
        string content,
        string sessionKey = "chat_direct",
        string channel = "cli",
        string chatId = "direct",
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        var preview = content.Length > 80 ? content[..80] + "..." : content;
        _logger?.LogInformation("[TIMING] Starting streaming request from {Channel}: {Preview}", channel, preview);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);
        sw.Stop();
        _logger?.LogInformation("[TIMING] GetOrCreateSessionAsync: {ElapsedMs}ms", sw.ElapsedMilliseconds);

        // Create CancellationTokenSource for this session
        var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sessionTokens.AddOrUpdate(sessionKey, _ => sessionCts, (_, _) => sessionCts);

        sw.Restart();
        var userMessage = BuildUserMessage(content);
        userMessage = userMessage.WithAgentRequestMessageSource(AgentRequestMessageSourceType.External, "user");
        _logger?.LogInformation("[DEBUG] Created user message with content length: {Length}, source type: External", content.Length);
        _logger?.LogInformation("[TIMING] Create user message: {ElapsedMs}ms", sw.ElapsedMilliseconds);

        // 自动提取标题：如果是第一条用户消息，使用消息内容作为标题
        await TryAutoSetSessionTitleAsync(session, sessionKey, content, cancellationToken);

        sw.Restart();
        _logger?.LogInformation("[TIMING] About to call GetAgentForSession and RunStreamingAsync...");

        try
        {
            await foreach (var update in StreamWithToolHintsAsync(session, userMessage, sessionKey, sessionCts.Token))
            {
                yield return update;
            }

            sw.Stop();
            _logger?.LogInformation("[TIMING] RunStreamingAsync completed: {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
        finally
        {
            _sessionTokens.TryRemove(sessionKey, out _);
            sessionCts.Dispose();

            try
            {
                await _sessionManager.SaveSessionAsync(session, sessionKey, CancellationToken.None);
                _logger?.LogInformation("[TIMING] Session saved after streaming");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save session after streaming for {SessionKey}", sessionKey);
            }

            _ = TryConsolidateMemoryAsync(session, sessionKey, CancellationToken.None).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger?.LogWarning(t.Exception, "Background memory consolidation failed");
            }, TaskContinuationOptions.OnlyOnFaulted);

            swTotal.Stop();
            _logger?.LogInformation("[TIMING] ProcessDirectStreamingAsync total: {ElapsedMs}ms", swTotal.ElapsedMilliseconds);
        }
    }

    private async IAsyncEnumerable<AgentResponseUpdate> StreamWithToolHintsAsync(
        AgentSession session,
        ChatMessage userMessage,
        string sessionKey,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var swInner = System.Diagnostics.Stopwatch.StartNew();
        var firstChunkReceived = false;

        var agent = GetAgentForSession(sessionKey);
        ToolExecutionContext.SetCurrentSessionKey(sessionKey);

        // Debug logging: Start a new request log and collect LLM request info
        var requestId = -1;
        if (_debugState?.IsDebugEnabled(sessionKey) == true)
        {
            requestId = await _debugState.StartRequestLogAsync(sessionKey, cancellationToken);
        }

        try
        {
            // Build LLM request info for debugging
            if (requestId > 0)
            {
                try
                {
                    await WriteLLMRequestDebugLogAsync(sessionKey, requestId, agent, session, userMessage, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to write debug log for LLM request");
                }
            }

            await foreach (var update in agent.RunStreamingAsync([userMessage], session, cancellationToken: cancellationToken))
            {
                swInner.Stop();
                if (!firstChunkReceived)
                {
                    firstChunkReceived = true;
                    _logger?.LogInformation("[TIMING] ★★★ FIRST CHUNK from agent.RunStreamingAsync: {ElapsedMs}ms ★★★", swInner.ElapsedMilliseconds);
                }
                else
                {
                    _logger?.LogInformation("[TIMING] Subsequent chunk: {ElapsedMs}ms, text: {Text}", swInner.ElapsedMilliseconds, update.Text?.Length > 50 ? update.Text[..50] + "..." : update.Text);
                }

                var functionCalls = update.Contents.OfType<FunctionCallContent>().ToList();
                if (functionCalls.Any())
                {
                    var toolHint = ToolHintFormatter.FormatToolHint(functionCalls);
                    if (!string.IsNullOrEmpty(toolHint))
                    {
                        var toolHintMarkdown = WrapToolHintAsMarkdown(toolHint);
                        var toolHintUpdate = new AgentResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = { new TextContent(toolHintMarkdown) },
                            AdditionalProperties = new()
                        };
                        toolHintUpdate.AdditionalProperties["_tool_hint"] = true;
                        
                        // 将 tool 信息序列化到 AdditionalProperties，供 WebUI 前端提取 ToolCallDetails
                        var firstCall = functionCalls.First();
                        var toolCallInfo = new Dictionary<string, object?>
                        {
                            ["name"] = firstCall.Name ?? "",
                            ["callId"] = firstCall.CallId ?? "",
                            ["arguments"] = firstCall.Arguments != null 
                                ? JsonSerializer.Serialize(firstCall.Arguments) 
                                : "{}"
                        };
                        toolHintUpdate.AdditionalProperties["_tool_call_info"] = JsonSerializer.Serialize(toolCallInfo);
                        
                        yield return toolHintUpdate;
                    }
                }

                // Handle tool results (FunctionResultContent) - emit them for CLI display
                var functionResults = update.Contents.OfType<FunctionResultContent>().ToList();
                if (functionResults.Any())
                {
                    foreach (var result in functionResults)
                    {
                        var toolResultText = FormatToolResult(result);
                        if (!string.IsNullOrEmpty(toolResultText))
                        {
                            var toolResultUpdate = new AgentResponseUpdate
                            {
                                Role = ChatRole.Tool,
                                Contents = { new TextContent(toolResultText) },
                                AdditionalProperties = new()
                            };
                            toolResultUpdate.AdditionalProperties["_tool_result"] = true;
                            toolResultUpdate.AdditionalProperties["tool_call_id"] = result.CallId ?? "unknown";
                            yield return toolResultUpdate;
                        }
                    }
                }

                var imageMarkdown = BuildSnapshotImageMarkdown(update.Contents);
                if (!string.IsNullOrWhiteSpace(imageMarkdown))
                {
                    _logger?.LogInformation("Snapshot markdown injected into streaming response for session {SessionKey}", sessionKey);
                    var imageUpdate = new AgentResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = { new TextContent(imageMarkdown) },
                        AdditionalProperties = new()
                    };
                    imageUpdate.AdditionalProperties["_snapshot_image"] = true;
                    yield return imageUpdate;
                }

                swInner.Restart();
                yield return update;
            }

            // Debug logging: Log final LLM response after streaming completes
            if (requestId > 0)
            {
                try
                {
                    var responseContent = CollectResponseContent(session);
                    await _debugState!.AppendToLogAsync(sessionKey, requestId, "\n---\n\n## OUT - LLM Response\n\n" + responseContent, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to write debug log for LLM response");
                }
            }
        }
        finally
        {
            ToolExecutionContext.SetCurrentSessionKey(null);
        }

        swInner.Stop();
        _logger?.LogInformation("[TIMING] RunStreamingAsync completed: {ElapsedMs}ms", swInner.ElapsedMilliseconds);
    }

    private async Task<OutboundMessage?> ProcessMessageAsync(
        InboundMessage msg,
        CancellationToken cancellationToken,
        string? overrideSessionKey = null)
    {
        var preview = msg.Content.Length > 80 ? msg.Content[..80] + "..." : msg.Content;
        _logger?.LogInformation("Processing message from {Channel}:{SenderId}: {Preview}", msg.Channel, msg.SenderId, preview);

        var sessionKey = overrideSessionKey ?? msg.SessionKey;

        if (msg.Channel == "system")
        {
            return await ProcessSystemMessageAsync(msg, cancellationToken);
        }

        // Try to handle as a command
        var commandResult = await TryHandleCommandAsync(msg, cancellationToken);
        if (commandResult != null)
        {
            return commandResult;
        }

        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);

        // Set runtime metadata in session state
        if (_runtimeMetadata.TryGetValue(sessionKey, out var metadata))
        {
            session.StateBag.SetValue("runtime:untrusted", JsonSerializer.Serialize(metadata));
        }

        // Create or reuse CancellationTokenSource for this session
        var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sessionTokens.AddOrUpdate(sessionKey, _ => sessionCts, (_, _) => sessionCts);

        var imageUrls = msg.Media?.Where(static m => !string.IsNullOrWhiteSpace(m)).ToArray();
        var userMessage = BuildUserMessage(msg.Content, imageUrls);
        userMessage = userMessage.WithAgentRequestMessageSource(AgentRequestMessageSourceType.External, "user");

        // 自动提取标题：如果是第一条用户消息，使用消息内容作为标题
        await TryAutoSetSessionTitleAsync(session, sessionKey, msg.Content, cancellationToken);

        AgentResponse response;
        ToolExecutionContext.SetCurrentSessionKey(sessionKey);
        try
        {
            var agent = GetAgentForSession(sessionKey);
            response = await agent.RunAsync([userMessage], session, cancellationToken: sessionCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Session {SessionKey} was cancelled", sessionKey);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent run failed for session {SessionKey}", sessionKey);
            throw;
        }
        finally
        {
            ToolExecutionContext.SetCurrentSessionKey(null);
            // Clean up session token
            _sessionTokens.TryRemove(sessionKey, out _);
            sessionCts.Dispose();
        }

        var responseText = response.Messages.FirstOrDefault()?.Text ?? "I've completed processing but have no response to give.";
        var snapshotImageMarkdown = BuildSnapshotImageMarkdown(response.Messages.SelectMany(m => m.Contents));
        if (!string.IsNullOrWhiteSpace(snapshotImageMarkdown))
        {
            _logger?.LogInformation("Snapshot markdown injected into non-streaming response for session {SessionKey}", sessionKey);
            responseText = string.IsNullOrWhiteSpace(responseText)
                ? snapshotImageMarkdown
                : $"{responseText}\n\n{snapshotImageMarkdown}";
        }

        preview = responseText.Length > 120 ? responseText[..120] + "..." : responseText;
        _logger?.LogInformation("Response to {Channel}:{SenderId}: {Preview}", msg.Channel, msg.SenderId, preview);

        await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);

        await TryConsolidateMemoryAsync(session, sessionKey, cancellationToken);

        return new OutboundMessage
        {
            Channel = msg.Channel,
            ChatId = msg.ChatId,
            Content = responseText,
            Metadata = msg.Metadata
        };
    }

    private string? BuildSnapshotImageMarkdown(IEnumerable<AIContent> contents)
    {
        var images = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var content in contents)
        {
            if (content is not FunctionResultContent functionResult)
            {
                continue;
            }

            var payload = GetFunctionResultPayload(functionResult);
            if (string.IsNullOrWhiteSpace(payload))
            {
                _logger?.LogWarning("BuildSnapshotImageMarkdown: Payload is empty");
                continue;
            }

            // _logger?.LogInformation("BuildSnapshotImageMarkdown Payload: {Payload}", payload);

                try
                {
                    using var document = JsonDocument.Parse(payload);
                    var rootElement = document.RootElement;

                    // Handle double-serialized JSON (e.g. payload is "{\"action\":...}")
                    // This can happen when the tool result is a JSON string that gets serialized again by the agent framework
                    if (rootElement.ValueKind == JsonValueKind.String)
                    {
                        var innerJson = rootElement.GetString();
                        if (!string.IsNullOrWhiteSpace(innerJson))
                        {
                            try
                            {
                                using var innerDoc = JsonDocument.Parse(innerJson);
                                ProcessSnapshotJsonElement(innerDoc.RootElement, seen, images);
                            }
                            catch (JsonException) { }
                        }
                    }
                    else
                    {
                        ProcessSnapshotJsonElement(rootElement, seen, images);
                    }
                }
                catch (JsonException)
                {
                }
            }

            if (images.Count == 0)
            {
                return null;
            }

            var lines = new List<string>();
            for (var i = 0; i < images.Count; i++)
            {
                var url = images[i];
                lines.Add($"![snapshot-{i + 1}]({url})");
            }

            return $"\n\n{string.Join("\n\n", lines)}\n\n";
        }

    private static string WrapToolHintAsMarkdown(string toolHint)
    {
        // 直接返回 Markdown 格式
        // 格式: [TOOL_CALL]tool_name("args")|||tool_name2("args")[/TOOL_CALL]
        return $"\n{toolHint}\n";
    }

    private void ProcessSnapshotJsonElement(JsonElement rootElement, HashSet<string> seen, List<string> images)
    {
        // Skip if the payload is not a JSON object
        if (rootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!TryGetJsonString(rootElement, "action", out var action) ||
            string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        if (!string.Equals(action, "snapshot", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action, "capture", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!TryGetJsonString(rootElement, "imagePath", out var imagePath) ||
            string.IsNullOrWhiteSpace(imagePath))
        {
            _logger?.LogWarning("BuildSnapshotImageMarkdown: ImagePath not found");
            return;
        }

        var imageUrl = ToSessionFileUrl(imagePath);
        if (string.IsNullOrWhiteSpace(imageUrl) || !seen.Add(imageUrl))
        {
            return;
        }

        images.Add(imageUrl);
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

    private static string? GetFunctionResultPayload(FunctionResultContent functionResult)
    {
        if (functionResult.Result == null)
        {
            return null;
        }

        if (functionResult.Result is string text)
        {
            return text;
        }

        if (functionResult.Result is JsonElement jsonElement)
        {
            return jsonElement.GetRawText();
        }

        try
        {
            return JsonSerializer.Serialize(functionResult.Result);
        }
        catch
        {
            return functionResult.Result.ToString();
        }
    }

    /// <summary>
    /// Formats a tool result for display in CLI
    /// </summary>
    private static string? FormatToolResult(FunctionResultContent functionResult)
    {
        var payload = GetFunctionResultPayload(functionResult);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        // Try to parse as JSON to extract meaningful information
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            // Handle different result formats
            if (root.ValueKind == JsonValueKind.Object)
            {
                // Check for error
                if (root.TryGetProperty("error", out var errorElement))
                {
                    var errorMsg = errorElement.GetString() ?? errorElement.GetRawText();
                    return $"[ERROR] {errorMsg}";
                }

                // Check for content/output
                if (root.TryGetProperty("content", out var contentElement))
                {
                    var content = contentElement.GetString() ?? contentElement.GetRawText();
                    return Truncate(content, 200);
                }

                if (root.TryGetProperty("output", out var outputElement))
                {
                    var output = outputElement.GetString() ?? outputElement.GetRawText();
                    return Truncate(output, 200);
                }

                // Check for action-based results (browser, etc.)
                if (root.TryGetProperty("action", out var actionElement))
                {
                    var action = actionElement.GetString();
                    if (root.TryGetProperty("url", out var urlElement))
                    {
                        var url = urlElement.GetString();
                        return $"{action}: {url}";
                    }
                    if (root.TryGetProperty("imagePath", out var imagePathElement))
                    {
                        var imagePath = imagePathElement.GetString();
                        return $"{action}: snapshot captured";
                    }
                    return action;
                }

                // For search results, show summary
                if (root.TryGetProperty("results", out var resultsElement) && resultsElement.ValueKind == JsonValueKind.Array)
                {
                    var count = resultsElement.GetArrayLength();
                    return $"Found {count} results";
                }

                // Default: show truncated JSON
                var json = root.GetRawText();
                return Truncate(json, 150);
            }

            // For string results
            if (root.ValueKind == JsonValueKind.String)
            {
                var str = root.GetString() ?? payload;
                return Truncate(str, 200);
            }
        }
        catch (JsonException)
        {
            // Not valid JSON, return as-is
        }

        // Fallback: return truncated payload
        return Truncate(payload, 200);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? "";
        }
        return value[..maxLength] + "…";
    }

    private string? ToSessionFileUrl(string? imagePath)
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

    private string? ToLocalSessionFilePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        if (imagePath.StartsWith("/api/files/sessions/", StringComparison.OrdinalIgnoreCase))
        {
            var relative = imagePath["/api/files/sessions/".Length..].TrimStart('/');
            return Path.Combine(_workspace.GetSessionsPath(), relative.Replace('/', Path.DirectorySeparatorChar));
        }

        if (Path.IsPathRooted(imagePath))
        {
            if (File.Exists(imagePath))
            {
                return imagePath;
            }

            var sessionsRoot = _workspace.GetSessionsPath();
            if (imagePath.StartsWith(sessionsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return imagePath;
            }

            return null;
        }

        var normalized = imagePath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("sessions/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["sessions/".Length..];
        }

        var sessionsRoot2 = _workspace.GetSessionsPath();
        var sessionPath = Path.Combine(sessionsRoot2, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(sessionPath))
        {
            return sessionPath;
        }

        var workspaceRoot = _workspace.GetWorkspacePath();
        var workspacePath = Path.Combine(workspaceRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(workspacePath))
        {
            return workspacePath;
        }

        return null;
    }

    private ChatMessage BuildUserMessage(string content, IEnumerable<string>? extraImageUrls = null)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrWhiteSpace(content))
        {
            contents.Add(new TextContent(content));
        }

        var imageUrls = ExtractMarkdownImageUrls(content);
        if (extraImageUrls != null)
        {
            imageUrls.AddRange(extraImageUrls.Where(static u => !string.IsNullOrWhiteSpace(u)));
        }

        foreach (var imageUrl in imageUrls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryLoadImageContent(imageUrl, out var imageContent))
            {
                continue;
            }

            contents.Add(imageContent);
        }

        return contents.Count switch
        {
            0 => new ChatMessage(ChatRole.User, string.Empty),
            1 when contents[0] is TextContent text => new ChatMessage(ChatRole.User, text.Text),
            _ => new ChatMessage(ChatRole.User, contents)
        };
    }

    private List<string> ExtractMarkdownImageUrls(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var urls = new List<string>();
        var matches = MarkdownImageRegex.Matches(content);
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var url = match.Groups["url"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(url))
            {
                urls.Add(url);
            }
        }

        return urls;
    }

    private bool TryLoadImageContent(string imageUrl, out DataContent imageContent)
    {
        imageContent = default!;
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return false;
        }

        var localPath = ToLocalSessionFilePath(imageUrl);
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(localPath);
            if (bytes.Length == 0)
            {
                return false;
            }

            var mediaType = GetImageMediaType(localPath);
            imageContent = new DataContent(bytes, mediaType);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load image content for LLM request: {ImageUrl}", imageUrl);
            return false;
        }
    }

    private string GetImageMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "image/png"
        };
    }

    private async Task<OutboundMessage?> ProcessSystemMessageAsync(
        InboundMessage msg,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Processing system message from {SenderId}", msg.SenderId);

        string originChannel;
        string originChatId;

        if (msg.ChatId.Contains(':'))
        {
            var parts = msg.ChatId.Split(':', 2);
            originChannel = parts[0];
            originChatId = parts[1];
        }
        else
        {
            originChannel = "cli";
            originChatId = msg.ChatId;
        }

        var sessionKey = $"{originChannel}:{originChatId}";
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);

        var systemMessage = new ChatMessage(ChatRole.User, $"[System: {msg.SenderId}] {msg.Content}");

        AgentResponse response;
        try
        {
            var agent = GetAgentForSession(sessionKey);
            response = await agent.RunAsync([systemMessage], session, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent run failed for system message");
            throw;
        }

        var responseText = response.Messages.FirstOrDefault()?.Text ?? "Background task completed.";

        await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);

        return new OutboundMessage
        {
            Channel = originChannel,
            ChatId = originChatId,
            Content = responseText
        };
    }

    private async Task<OutboundMessage> HandleNewSessionCommandAsync(
        InboundMessage msg,
        AgentSession existingSession,
        CancellationToken cancellationToken)
    {
        var sessionKey = msg.SessionKey;

        if (_memoryStore != null)
        {
            try
            {
                var chatClient = GetChatClientFromAgent(sessionKey);
                if (chatClient != null)
                {
                    var consolidator = new MemoryConsolidator(
                        chatClient,
                        _memoryStore,
                        _workspace,
                        _memoryWindow,
                        null);

                    var messages = GetSessionMessages(existingSession);
                    await consolidator.ConsolidateAsync(messages, 0, archiveAll: true, cancellationToken);
                    _logger?.LogInformation("Memory consolidation completed for /new command");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to consolidate memory for /new command");
            }
        }

        await _sessionManager.ClearSessionAsync(sessionKey, cancellationToken);

        return new OutboundMessage
        {
            Channel = msg.Channel,
            ChatId = msg.ChatId,
            Content = "New session started."
        };
    }

    private async Task<OutboundMessage?> TryHandleCommandAsync(InboundMessage msg, CancellationToken cancellationToken)
    {
        var content = msg.Content.Trim();
        var commandName = content.StartsWith('/')
            ? content.Split(' ')[0].ToLowerInvariant()
            : content.ToLowerInvariant();

        if (!_commands.TryGetValue(commandName, out var command))
        {
            return null;
        }

        // Immediate commands are handled without agent processing
        if (command.Immediate)
        {
            return await command.Handler(msg, cancellationToken);
        }

        // Non-immediate commands (like /new) are also handled directly
        // This is for backward compatibility - /new needs session access
        return await command.Handler(msg, cancellationToken);
    }

    public async Task<bool> TryCancelSessionAsync(string sessionKey)
    {
        var cancelled = false;

        // Cancel the agent task
        if (_sessionTokens.TryGetValue(sessionKey, out var cts))
        {
            cts.Cancel();
            _logger?.LogInformation("Cancelled session {SessionKey}", sessionKey);
            cancelled = true;
        }

        // Cancel subagents for this session
        if (_subagentManager != null)
        {
            if (_subagentManager.CancelSession(sessionKey))
            {
                _logger?.LogInformation("Cancelled subagents for session {SessionKey}", sessionKey);
                cancelled = true;
            }
        }

        if (!cancelled)
        {
            _logger?.LogDebug("No active session found for {SessionKey}", sessionKey);
        }

        await Task.Delay(0); // Allow cancellation to propagate
        return cancelled;
    }

    public void SetRuntimeMetadata(string sessionKey, IReadOnlyDictionary<string, string> metadata)
    {
        _runtimeMetadata[sessionKey] = metadata;
        _logger?.LogDebug("Set runtime metadata for session {SessionKey}", sessionKey);
    }

    public IReadOnlyDictionary<string, string>? GetRuntimeMetadata(string sessionKey)
    {
        return _runtimeMetadata.TryGetValue(sessionKey, out var metadata) ? metadata : null;
    }

    private IChatClient? GetChatClientFromAgent(string? sessionKey = null)
    {
        if (!string.IsNullOrEmpty(sessionKey))
        {
            var agent = GetAgentForSession(sessionKey);
            
            if (agent == null)
            {
                return null;
            }
            
            var client = agent.GetChatClient();
            
            if (client == null)
            {
                // 尝试直接反射获取
                var chatClientField = typeof(Microsoft.Agents.AI.ChatClientAgent)
                    .GetField("_chatClient", BindingFlags.NonPublic | BindingFlags.Instance);
                if (chatClientField != null)
                {
                    client = chatClientField.GetValue(agent) as IChatClient;
                }
            }
            
            return client;
        }
        
        var defaultClient = _defaultAgent?.GetChatClient();
        return defaultClient;
    }

    private List<ChatMessage> GetSessionMessages(AgentSession session)
    {
        return session.GetAllMessages().ToList();
    }

    private async Task TryConsolidateMemoryAsync(AgentSession session, string sessionKey, CancellationToken cancellationToken)
    {
        if (_memoryStore == null)
            return;

        try
        {
            var messages = GetSessionMessages(session);
            if (messages.Count <= _memoryWindow)
            {
                _logger?.LogDebug("Memory consolidation skipped: {Count} messages <= window {Window}", messages.Count, _memoryWindow);
                return;
            }

            var chatClient = GetChatClientFromAgent(sessionKey);
            if (chatClient == null)
            {
                _logger?.LogWarning("Could not get ChatClient for memory consolidation");
                return;
            }

            var consolidator = new MemoryConsolidator(
                chatClient,
                _memoryStore,
                _workspace,
                _memoryWindow);

            _logger?.LogInformation("Starting memory consolidation for {Count} messages", messages.Count);
            var lastConsolidated = _sessionManager.GetLastConsolidated(sessionKey);
            var newLastConsolidated = await consolidator.ConsolidateAsync(messages, lastConsolidated, archiveAll: false, cancellationToken);

            if (newLastConsolidated.HasValue)
            {
                _sessionManager.SetLastConsolidated(sessionKey, newLastConsolidated.Value);
                await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);
            }
            _logger?.LogInformation("Memory consolidation completed");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Memory consolidation failed");
        }
    }

    private async Task TryAutoSetSessionTitleAsync(AgentSession session, string sessionKey, string userContent, CancellationToken cancellationToken)
    {
        try
        {
            // 获取当前标题
            var currentTitle = _sessionManager.GetSessionTitle(sessionKey);

            // 如果标题已设置（非空且不是默认格式），则不自动更新
            if (!string.IsNullOrEmpty(currentTitle) && !IsDefaultSessionTitle(currentTitle))
            {
                return;
            }

            // 获取会话中的消息数量
            var messages = GetSessionMessages(session);

            // 如果这是第一条用户消息（session 中还没有消息），则使用内容作为标题
            if (messages.Count == 0)
            {
                string newTitle;
                
                if (userContent.Length > 50)
                {
                    // 如果消息超过 50 字符，使用 LLM 生成标题
                    newTitle = await GenerateTitleWithLLMAsync(sessionKey, userContent, cancellationToken);
                    
                    // 如果 LLM 生成失败，回退到截断方式
                    if (string.IsNullOrWhiteSpace(newTitle))
                    {
                        newTitle = userContent.Substring(0, 50) + "...";
                    }
                }
                else
                {
                    // 短消息直接使用截断方式
                    newTitle = userContent;
                }

                if (!string.IsNullOrWhiteSpace(newTitle))
                {
                    _sessionManager.SetSessionTitle(sessionKey, newTitle);
                    // 立即保存以更新文件中的标题
                    await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);
                    _logger?.LogInformation("Auto-set session title for {SessionKey} to: {Title}", sessionKey, newTitle);
                }
            }
            // 如果标题仍为默认值，但会话中已有消息，用第一条用户消息回填
            else if (messages.Count > 0 && IsDefaultSessionTitle(currentTitle))
            {
                var firstUserMessage = messages.FirstOrDefault(m => m.Role == ChatRole.User);
                if (firstUserMessage?.Text is not null)
                {
                    var firstContent = firstUserMessage.Text;
                    string newTitle;

                    if (firstContent.Length > 50)
                    {
                        newTitle = await GenerateTitleWithLLMAsync(sessionKey, firstContent, cancellationToken);
                        if (string.IsNullOrWhiteSpace(newTitle))
                        {
                            newTitle = firstContent.Substring(0, 50) + "...";
                        }
                    }
                    else
                    {
                        newTitle = firstContent;
                    }

                    if (!string.IsNullOrWhiteSpace(newTitle))
                    {
                        _sessionManager.SetSessionTitle(sessionKey, newTitle);
                        await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);
                        _logger?.LogInformation("Backfilled session title for {SessionKey} to: {Title}", sessionKey, newTitle);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to auto-set session title for {SessionKey}", sessionKey);
        }
    }

    private async Task<string> GenerateTitleWithLLMAsync(string sessionKey, string userContent, CancellationToken cancellationToken)
    {
        try
        {
            var chatClient = GetChatClientFromAgent(sessionKey);
            if (chatClient == null)
            {
                _logger?.LogWarning("No chat client available for session {SessionKey}", sessionKey);
                return string.Empty;
            }

            var systemPrompt = "You are a title generator. Generate a very short title (max 10 characters, Chinese OK) for the user's message. ONLY output the title, nothing else. No punctuation, no quotes, no explanation.";
            
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userContent)
                ],
                cancellationToken: cancellationToken);

            var title = response.Messages.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
            
            // 清理标题：移除可能的标点符号和引号
            title = title.Trim('"', '\'', '。', '.', '！', '!', '？', '?', ' ', '\n', '\r');
            
            // 如果标题为空或和原消息完全一样，说明 LLM 没有正确处理，回退到截断
            if (string.IsNullOrWhiteSpace(title) || title == userContent)
            {
                return string.Empty; // 信号回退
            }
            
            return title;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to generate title with LLM for session {SessionKey}", sessionKey);
            return string.Empty;
        }
    }

    /// <summary>
    /// 检查标题是否为默认格式（如"会话 03-16 14:30"）
    /// </summary>
    private static bool IsDefaultSessionTitle(string title)
    {
        return !string.IsNullOrEmpty(title) && DefaultSessionTitleRegex.IsMatch(title);
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _runningCts?.Dispose();
        _disposed = true;

        _logger?.LogInformation("Agent runtime disposed");
    }

    /// <summary>
    /// 清除 agent 缓存，使配置修改后强制重新创建 agent
    /// </summary>
    public void ClearAgentCache()
    {
        _profileAgents.Clear();
        _logger?.LogInformation("Agent cache cleared");
    }

    /// <summary>
    /// 获取指定 profile 的 ChatClientAgent，如果未指定则使用默认 agent
    /// </summary>
    private ChatClientAgent GetAgentForSession(string sessionKey)
    {
        // 如果没有配置 profile 支持，直接返回默认 agent
        if (_chatClientFactory == null || _llmConfig == null)
        {
            return _defaultAgent;
        }

        // 获取会话的 profile ID
        var profileId = _sessionManager.GetSessionProfileId(sessionKey);
        if (string.IsNullOrEmpty(profileId))
        {
            return _defaultAgent;
        }

        // 检查是否是默认 profile
        if (profileId.Equals(_llmConfig.DefaultProfile, StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrEmpty(_llmConfig.DefaultProfile) && profileId == "default"))
        {
            return _defaultAgent;
        }

        // 从缓存获取或创建该 profile 的 agent
        return _profileAgents.GetOrAdd(profileId, _ => CreateAgentForProfile(profileId));
    }

    /// <summary>
    /// 为指定 profile 创建新的 ChatClientAgent
    /// </summary>
    private ChatClientAgent CreateAgentForProfile(string profileId)
    {
        _logger?.LogInformation("Creating ChatClientAgent for profile: {ProfileId}", profileId);

        if (_llmConfig == null || _chatClientFactory == null || _serviceProvider == null)
        {
            _logger?.LogWarning("Required services not available for profile {ProfileId}, using default agent", profileId);
            return _defaultAgent;
        }

        if (!_llmConfig.Profiles.TryGetValue(profileId, out var profile))
        {
            _logger?.LogWarning("Profile {ProfileId} not found, using default agent", profileId);
            return _defaultAgent;
        }

        try
        {
            var chatClient = _chatClientFactory.CreateChatClient(
                profile.Provider ?? "openai",
                profile.Model,
                profile.ApiKey,
                profile.ApiBase);

            var workspace = _serviceProvider.GetRequiredService<IWorkspaceManager>();
            var skillsLoader = _serviceProvider.GetRequiredService<ISkillsLoader>();
            var memoryStore = _serviceProvider.GetService<IMemoryStore>();
            var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();

            var agentOptions = new AgentOptions
            {
                Temperature = (float)profile.Temperature,
                MaxTokens = profile.MaxTokens
            };

            return NanoBotAgentFactory.Create(
                chatClient,
                workspace,
                skillsLoader,
                null, // tools will be resolved from DI in factory
                loggerFactory,
                agentOptions,
                memoryStore,
                _memoryWindow);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create agent for profile {ProfileId}, using default agent", profileId);
            return _defaultAgent;
        }
    }

    private async Task WriteLLMRequestDebugLogAsync(
        string sessionKey,
        int requestId,
        ChatClientAgent agent,
        AgentSession session,
        ChatMessage userMessage,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        // Get LLM configuration info
        var profileId = _sessionManager.GetSessionProfileId(sessionKey);
        LlmProfile? profile = null;
        if (!string.IsNullOrEmpty(profileId) && _llmConfig?.Profiles != null)
        {
            _llmConfig.Profiles.TryGetValue(profileId, out profile);
        }

        sb.AppendLine("### LLM Config");
        if (profile != null)
        {
            sb.AppendLine($"- **Model**: {profile.Model}");
            sb.AppendLine($"- **Provider**: {profile.Provider ?? "openai"}");
            sb.AppendLine($"- **API Base**: {profile.ApiBase ?? "(default)"}");
            sb.AppendLine($"- **Temperature**: {profile.Temperature}");
            sb.AppendLine($"- **Max Tokens**: {profile.MaxTokens}");
        }
        else
        {
            sb.AppendLine("- *(Using default agent configuration)*");
        }
        sb.AppendLine();

        // Get system prompt / instructions from agent
        var instructions = NanoBotAgentFactory.BuildInstructions(_workspace, null);
        sb.AppendLine("### System Prompt (Instructions)");
        sb.AppendLine("```");
        sb.AppendLine(instructions);
        sb.AppendLine("```");
        sb.AppendLine();

        // Get tools from agent's ChatOptions
        var chatClient = agent.GetChatClient();
        if (chatClient != null)
        {
            var reflectionField = chatClient.GetType().GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (reflectionField?.GetValue(chatClient) is Microsoft.Extensions.AI.ChatOptions chatOptions && chatOptions.Tools != null)
            {
                sb.AppendLine("### Tools");
                foreach (var tool in chatOptions.Tools)
                {
                    var toolName = tool.Name ?? "unknown";
                    var toolDesc = tool.Description ?? "";
                    sb.AppendLine($"- **{toolName}**: {toolDesc}");
                }
                sb.AppendLine();
            }
        }

        // Build messages
        sb.AppendLine("### Messages");
        sb.AppendLine("| Role | Content |");
        sb.AppendLine("|------|---------|");

        foreach (var msg in session.GetAllMessages())
        {
            var role = msg.Role.Value ?? "unknown";
            var text = TruncateForDebug(msg.Text, 200);
            var hasFunctionCalls = msg.Contents.Any(c => c is FunctionCallContent);
            var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            var functionSummary = "";

            if (hasFunctionCalls && functionCalls.Any())
            {
                functionSummary = " [tool_calls: " + string.Join(", ", functionCalls.Select(f => f.Name ?? "?")) + "]";
            }

            sb.AppendLine($"| {role} | {EscapeForMarkdown(text)}{functionSummary} |");
        }

        var userText = TruncateForDebug(userMessage.Text, 200);
        var userHasFunctionCalls = userMessage.Contents.Any(c => c is FunctionCallContent);
        sb.AppendLine($"| user | {EscapeForMarkdown(userText)}{(userHasFunctionCalls ? " [tool_calls]" : "")} |");

        await _debugState!.AppendToLogAsync(sessionKey, requestId, sb.ToString(), cancellationToken);
    }

    private string CollectResponseContent(AgentSession session)
    {
        var sb = new StringBuilder();

        var messages = session.GetAllMessages().ToList();
        var recentMessages = messages.Skip(Math.Max(0, messages.Count - 10));

        foreach (var msg in recentMessages)
        {
            var role = msg.Role.Value ?? "unknown";
            var text = msg.Text ?? "";

            if (role == "assistant" && !string.IsNullOrEmpty(text))
            {
                sb.AppendLine(text);
                sb.AppendLine();
            }

            var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            foreach (var call in functionCalls)
            {
                var argsStr = call.Arguments != null ? JsonSerializer.Serialize(call.Arguments) : "{}";
                sb.AppendLine($"[tool_call: {call.Name} {argsStr}]");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string TruncateForDebug(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private static string EscapeForMarkdown(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        return text
            .Replace("|", "\\|")
            .Replace("\n", " ")
            .Replace("**", "\\*\\*");
    }
}
