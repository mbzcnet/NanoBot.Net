using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;
using NanoBot.Core.Bus;
using NanoBot.Core.Tools;

namespace NanoBot.Agent.Services;

/// <summary>
/// Handles streaming message processing with tool hints and debug logging.
/// </summary>
public sealed class StreamingProcessor
{
    private readonly ChatClientAgent _defaultAgent;
    private readonly ISessionManager _sessionManager;
    private readonly MemoryConsolidationService _memoryConsolidationService;
    private readonly SessionTitleManager _sessionTitleManager;
    private readonly ImageContentProcessor _imageProcessor;
    private readonly ILogger<StreamingProcessor>? _logger;
    private readonly Func<string, ChatClientAgent> _getAgentForSession;
    private readonly Func<string, IChatClient?> _getChatClient;
    private readonly Action<string>? _setSessionToken;

#if DEBUG
    private readonly Debug.DebugLogger? _debugLogger;
#endif

    public StreamingProcessor(
        ChatClientAgent defaultAgent,
        ISessionManager sessionManager,
        MemoryConsolidationService memoryConsolidationService,
        SessionTitleManager sessionTitleManager,
        ImageContentProcessor imageProcessor,
        Func<string, ChatClientAgent> getAgentForSession,
        Func<string, IChatClient?> getChatClient,
        Action<string>? setSessionToken = null,
#if DEBUG
        Debug.DebugLogger? debugLogger = null,
#endif
        ILogger<StreamingProcessor>? logger = null)
    {
        _defaultAgent = defaultAgent;
        _sessionManager = sessionManager;
        _memoryConsolidationService = memoryConsolidationService;
        _sessionTitleManager = sessionTitleManager;
        _imageProcessor = imageProcessor;
        _getAgentForSession = getAgentForSession;
        _getChatClient = getChatClient;
        _setSessionToken = setSessionToken;
#if DEBUG
        _debugLogger = debugLogger;
#endif
        _logger = logger;
    }

    /// <summary>
    /// Processes a streaming request from the CLI/WebUI.
    /// </summary>
    public async IAsyncEnumerable<AgentResponseUpdate> ProcessDirectStreamingAsync(
        string content,
        string sessionKey = "chat_direct",
        string channel = "cli",
        string chatId = "direct",
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var swTotal = Stopwatch.StartNew();
        var preview = content.Length > 80 ? content[..80] + "..." : content;
        _logger?.LogInformation("[TIMING] Starting streaming request from {Channel}: {Preview}", channel, preview);

        var sw = Stopwatch.StartNew();
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);
        sw.Stop();
        _logger?.LogInformation("[TIMING] GetOrCreateSessionAsync: {ElapsedMs}ms", sw.ElapsedMilliseconds);

        // Create CancellationTokenSource for this session
        var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _setSessionToken?.Invoke(sessionKey);

        sw.Restart();
        var userMessage = _imageProcessor.BuildUserMessage(content);
        userMessage = userMessage.WithAgentRequestMessageSource(AgentRequestMessageSourceType.External, "user");
        _logger?.LogInformation("[TIMING] Create user message: {ElapsedMs}ms", sw.ElapsedMilliseconds);

        // Auto-extract title for first message
        await _sessionTitleManager.TryAutoSetSessionTitleAsync(session, sessionKey, content, cancellationToken);

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

            _ = _memoryConsolidationService.TryConsolidateAsync(session, sessionKey, CancellationToken.None).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger?.LogWarning(t.Exception, "Background memory consolidation failed");
            }, TaskContinuationOptions.OnlyOnFaulted);

            swTotal.Stop();
            _logger?.LogInformation("[TIMING] ProcessDirectStreamingAsync total: {ElapsedMs}ms", swTotal.ElapsedMilliseconds);
        }
    }

    // Session cancellation tokens storage
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessionTokens = new(StringComparer.Ordinal);

    private async IAsyncEnumerable<AgentResponseUpdate> StreamWithToolHintsAsync(
        AgentSession session,
        ChatMessage userMessage,
        string sessionKey,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var swInner = Stopwatch.StartNew();
        var firstChunkReceived = false;

        var agent = _getAgentForSession(sessionKey);
        ToolExecutionContext.SetCurrentSessionKey(sessionKey);

        // Debug logging: Start a new request log
        var requestId = -1;
        var debugLogCompleted = false;
#if DEBUG
        if (_debugLogger?.IsDebugEnabled(sessionKey) == true)
        {
            requestId = await _debugLogger.StartRequestLogAsync(sessionKey, cancellationToken);
        }

        // Debug logging: Write LLM request info
        if (requestId > 0)
        {
            try
            {
                await _debugLogger!.WriteLLMRequestDebugLogAsync(sessionKey, requestId, agent, session, userMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to write debug log for LLM request");
            }
        }
#endif

        // Accumulate response content for debug logging
        var responseContentBuilder = new StringBuilder();

        // Timing tracking for debug logging
        var requestTime = DateTime.UtcNow;
        DateTime? responseStartTime = null;
        DateTime? responseEndTime = null;

        try
        {
            await foreach (var update in agent.RunStreamingAsync([userMessage], session, cancellationToken: cancellationToken))
            {
                swInner.Stop();
                if (!firstChunkReceived)
                {
                    firstChunkReceived = true;
                    responseStartTime = DateTime.UtcNow;
                    _logger?.LogInformation("[TIMING] FIRST CHUNK from agent.RunStreamingAsync: {ElapsedMs}ms", swInner.ElapsedMilliseconds);
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
                        var toolHintMarkdown = ToolHintFormatter.WrapToolHintAsMarkdown(toolHint);
                        var toolHintUpdate = new AgentResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = { new TextContent(toolHintMarkdown) },
                            AdditionalProperties = new()
                        };
                        toolHintUpdate.AdditionalProperties["_tool_hint"] = true;

                        // Serialize tool call info for frontend
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

                // Handle tool results for CLI display
                var functionResults = update.Contents.OfType<FunctionResultContent>().ToList();
                if (functionResults.Any())
                {
                    foreach (var result in functionResults)
                    {
                        var toolResultText = ToolHintFormatter.FormatToolResult(result);
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

                // Extract snapshot images
                var imageContexts = _imageProcessor.ExtractSnapshotImageContext(update.Contents);
                if (imageContexts != null && imageContexts.Length > 0)
                {
                    _logger?.LogInformation("Snapshot images extracted for session {SessionKey}: {Count} images", sessionKey, imageContexts.Length);
                    var imageUpdate = new AgentResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = { new TextContent(string.Empty) },
                        AdditionalProperties = new()
                    };
                    imageUpdate.AdditionalProperties["_snapshot_images"] = imageContexts;
                    yield return imageUpdate;
                }

                // Accumulate response text for debug
                if (!string.IsNullOrEmpty(update.Text))
                {
                    responseContentBuilder.Append(update.Text);
                }

                // Accumulate tool calls for debug
                foreach (var call in functionCalls)
                {
                    var argsStr = call.Arguments != null ? JsonSerializer.Serialize(call.Arguments) : "{}";
                    responseContentBuilder.AppendLine($"```\nact tool call {call.Name}\ncommand: {argsStr}\n```");
                    responseContentBuilder.AppendLine();
                }

                swInner.Restart();
                yield return update;
            }

            responseEndTime = DateTime.UtcNow;

#if DEBUG
            // Debug logging: Log final LLM response
            if (requestId > 0)
            {
                try
                {
                    var responseContent = responseContentBuilder.ToString();
                    await _debugLogger!.WriteLLMResponseDebugLogAsync(
                        sessionKey, requestId, responseContent, requestTime, responseStartTime, responseEndTime.Value, cancellationToken);
                    await _debugLogger.FinishRequestLogAsync(sessionKey, requestId, "Stream completed normally", cancellationToken);
                    debugLogCompleted = true;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to write debug log for LLM response");
                }
            }
#endif
        }
        finally
        {
            ToolExecutionContext.SetCurrentSessionKey(null);

#if DEBUG
            // Ensure debug log is marked as ended even if exception occurred
            if (requestId > 0 && _debugLogger?.IsDebugEnabled(sessionKey) == true && !debugLogCompleted)
            {
                _debugLogger.FinishRequestLogSync(sessionKey, requestId, "Stream ended (abnormal/completed in finally)");
            }
#endif
        }

        swInner.Stop();
        _logger?.LogInformation("[TIMING] RunStreamingAsync completed: {ElapsedMs}ms", swInner.ElapsedMilliseconds);
    }
}
