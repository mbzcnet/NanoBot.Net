using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Agent;
using NanoBot.Core.Configuration;
using NanoBot.Core.Sessions;
using AgentSessionManager = NanoBot.Agent.ISessionManager;

namespace NanoBot.WebUI.Services;

// ToolCallInfo 在 Core 项目中定义
// AgentResponseChunk 在 Core 项目中定义

public class AgentService : IAgentService
{
    private readonly IAgentRuntime _agentRuntime;
    private readonly AgentSessionManager _sessionManager;
    private readonly LlmConfig? _llmConfig;
    private readonly ILogger<AgentService> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeSessions;

    public AgentService(
        IAgentRuntime agentRuntime,
        AgentSessionManager sessionManager,
        LlmConfig? llmConfig,
        ILogger<AgentService> logger)
    {
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _llmConfig = llmConfig;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _activeSessions = new ConcurrentDictionary<string, CancellationTokenSource>();
    }

    public async Task<string> SendMessageAsync(
        string sessionId, 
        string message, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending message to session {SessionId}", sessionId);
        
        try
        {
            var sessionKey = $"chat_{sessionId}";
            var response = await _agentRuntime.ProcessDirectAsync(
                message, 
                sessionKey, 
                "webui", 
                sessionId, 
                cancellationToken);
            
            _logger.LogInformation("Received response for session {SessionId}", sessionId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to session {SessionId}", sessionId);
            throw;
        }
    }

    public async IAsyncEnumerable<AgentResponseChunk> SendMessageStreamingAsync(
        string sessionId, 
        string message, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending streaming message to session {SessionId}", sessionId);
        
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeSessions[sessionId] = cts;
        
        var sessionKey = $"chat_{sessionId}";
        Exception? error = null;
        
        IAsyncEnumerable<Microsoft.Agents.AI.AgentResponseUpdate>? stream = null;
        try
        {
            stream = _agentRuntime.ProcessDirectStreamingAsync(
                message, 
                sessionKey, 
                "webui", 
                sessionId, 
                cts.Token);
        }
        catch (Exception ex)
        {
            error = ex;
            _logger.LogError(ex, "Error starting streaming for session {SessionId}", sessionId);
        }
        
        if (error != null)
        {
            // 构建包含 LLM 配置信息的错误消息
            var errorMessage = FormatErrorWithLlmConfig(sessionKey, error.Message);
            yield return new AgentResponseChunk(
                Content: $"\n\n[错误: {errorMessage}]",
                IsComplete: true
            );
            _activeSessions.TryRemove(sessionId, out _);
            cts.Dispose();
            yield break;
        }
        
        if (stream != null)
        {
            var cancelled = false;
            Exception? streamError = null;

            // 使用 await foreach 遍历流，捕获可能的异常
            await using var enumerator = stream.GetAsyncEnumerator(cts.Token);
            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync())
                        break;
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    streamError = ex;
                    _logger.LogError(ex, "Error during streaming for session {SessionId}", sessionId);
                    break;
                }

                if (cts.Token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                var update = enumerator.Current;
                var text = GetUpdateText(update);
                var functionCalls = update.Contents.OfType<FunctionCallContent>().ToList();
                var toolCall = functionCalls.FirstOrDefault()?.Name;
                var isToolResult = update.AdditionalProperties?.ContainsKey("_tool_result") == true;
                var toolResultCallId = isToolResult && update.AdditionalProperties != null &&
                                       update.AdditionalProperties.TryGetValue("tool_call_id", out var toolCallIdValue)
                    ? toolCallIdValue?.ToString()
                    : null;
                
                // 检测是否为 tool-hint（仅用于展示 tool 调用信息的标记，不应显示在正文）
                var isToolHint = update.AdditionalProperties?.ContainsKey("_tool_hint") == true;
                
                // 构建详细的工具调用信息
                ToolCallInfo? toolCallDetails = null;
                if (functionCalls.Any())
                {
                    var firstCall = functionCalls.First();
                    var argsJson = firstCall.Arguments != null
                        ? JsonSerializer.Serialize(firstCall.Arguments)
                        : "{}";
                    toolCallDetails = new ToolCallInfo(
                        Name: firstCall.Name ?? "",
                        Arguments: argsJson,
                        CallId: firstCall.CallId
                    );
                }
                else if (isToolHint && update.AdditionalProperties != null)
                {
                    // 从 AgentRuntime 序列化的 _tool_call_info 中提取 tool call 信息
                    if (update.AdditionalProperties.TryGetValue("_tool_call_info", out var toolInfoValue) && toolInfoValue != null)
                    {
                        try
                        {
                            var toolInfoJson = toolInfoValue.ToString();
                            if (!string.IsNullOrEmpty(toolInfoJson))
                            {
                                var toolInfo = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolInfoJson);
                                if (toolInfo != null)
                                {
                                    var name = toolInfo.TryGetValue("name", out var nameEl) ? nameEl.GetString() : null;
                                    var callId = toolInfo.TryGetValue("callId", out var callIdEl) ? callIdEl.GetString() : null;
                                    var args = toolInfo.TryGetValue("arguments", out var argsEl) ? argsEl.GetString() : "{}";
                                    toolCallDetails = new ToolCallInfo(
                                        Name: name ?? "",
                                        Arguments: args,
                                        CallId: callId
                                    );
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to deserialize tool call info from _tool_call_info");
                        }
                    }
                }

                // tool-hint 不应有正文内容（正文中的 [TOOL_CALL] 标记会在前端被过滤，不应发送到前端）
                var content = isToolHint ? string.Empty : text;

                yield return new AgentResponseChunk(
                    Content: content,
                    IsComplete: false,
                    ToolCall: toolCall,
                    ToolCallDetails: toolCallDetails,
                    IsToolResult: isToolResult,
                    ToolResultCallId: toolResultCallId,
                    IsToolHint: isToolHint
                );
            }

            if (streamError != null)
            {
                // 构建包含 LLM 配置信息的错误消息
                var errorMessage = FormatErrorWithLlmConfig(sessionKey, streamError.Message);
                yield return new AgentResponseChunk(
                    Content: $"\n\n[错误: {errorMessage}]",
                    IsComplete: true
                );
            }
            else if (cancelled)
            {
                _logger.LogInformation("Streaming cancelled for session {SessionId}", sessionId);
                yield return new AgentResponseChunk(
                    Content: "\n\n[已停止生成]",
                    IsComplete: true
                );
            }
            else
            {
                _logger.LogInformation("Streaming complete for session {SessionId}", sessionId);
                yield return new AgentResponseChunk(
                    Content: string.Empty,
                    IsComplete: true
                );
            }
        }
        
        _activeSessions.TryRemove(sessionId, out _);
        cts.Dispose();
    }

    private static string GetUpdateText(Microsoft.Agents.AI.AgentResponseUpdate update)
    {
        // 首先检查 Text 属性
        if (!string.IsNullOrWhiteSpace(update.Text))
        {
            return update.Text;
        }

        // 尝试从 Contents 中提取文本 - 支持多种文本内容类型
        var textParts = new List<string>();
        foreach (var content in update.Contents)
        {
            if (content == null) continue;

            // 尝试获取 Text 属性（适用于 TextContent、StringContent 等）
            var textProperty = content.GetType().GetProperty("Text");
            if (textProperty != null)
            {
                var text = textProperty.GetValue(content) as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    textParts.Add(text);
                }
            }
        }

        return textParts.Count == 0 ? string.Empty : string.Join(string.Empty, textParts);
    }

    public void StopGeneration(string sessionId)
    {
        _logger.LogInformation("Stopping generation for session {SessionId}", sessionId);

        // 使用正确的 sessionKey 格式，与 AgentRuntime 保持一致
        var sessionKey = $"chat_{sessionId}";

        // 取消 AgentService 管理的 CTS
        if (_activeSessions.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        // 同时尝试取消 AgentRuntime 中的会话
        try
        {
            _agentRuntime.TryCancelSessionAsync(sessionKey).Wait(TimeSpan.FromMilliseconds(100));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cancel AgentRuntime session {SessionKey}", sessionKey);
        }
    }

    /// <summary>
    /// 格式化错误消息，包含 LLM 配置信息（不包含 API Key）
    /// </summary>
    private string FormatErrorWithLlmConfig(string sessionKey, string originalError)
    {
        try
        {
            // 获取会话的 Profile ID
            var profileId = _sessionManager.GetSessionProfileId(sessionKey);
            if (string.IsNullOrEmpty(profileId) || _llmConfig == null)
            {
                return originalError;
            }

            // 获取 Profile 配置
            if (!_llmConfig.Profiles.TryGetValue(profileId, out var profile))
            {
                return $"{originalError} [Profile: {profileId}]";
            }

            // 构建配置信息字符串（不包含 API Key）
            var configInfo = $"[Profile: {profileId}, Provider: {profile.Provider ?? "unknown"}, Model: {profile.Model}";

            if (!string.IsNullOrWhiteSpace(profile.ApiBase))
            {
                configInfo += $", ApiBase: {profile.ApiBase}";
            }

            configInfo += "]";

            return $"{originalError} {configInfo}";
        }
        catch
        {
            // 如果构建错误信息失败，返回原始错误
            return originalError;
        }
    }
}
