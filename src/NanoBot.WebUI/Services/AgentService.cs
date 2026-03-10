using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Agent;
using NanoBot.Core.Configuration;
using NanoBot.Core.Sessions;

namespace NanoBot.WebUI.Services;

// ToolCallInfo 在 Core 项目中定义
// AgentResponseChunk 在 Core 项目中定义

public class AgentService : IAgentService
{
    private readonly IAgentRuntime _agentRuntime;
    private readonly ISessionManager _sessionManager;
    private readonly LlmConfig? _llmConfig;
    private readonly ILogger<AgentService> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeSessions;

    public AgentService(
        IAgentRuntime agentRuntime,
        ISessionManager sessionManager,
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
            var sessionKey = $"webui:{sessionId}";
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
        
        var sessionKey = $"webui:{sessionId}";
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

                // 构建详细的工具调用信息
                ToolCallInfo? toolCallDetails = null;
                if (functionCalls.Any())
                {
                    var firstCall = functionCalls.First();
                    var argsJson = firstCall.Arguments?.ToString() ?? "{}";
                    toolCallDetails = new ToolCallInfo(
                        Name: firstCall.Name ?? "",
                        Arguments: argsJson,
                        CallId: firstCall.Id
                    );
                }

                yield return new AgentResponseChunk(
                    Content: text,
                    IsComplete: false,
                    ToolCall: toolCall,
                    ToolCallDetails: toolCallDetails
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
        if (!string.IsNullOrWhiteSpace(update.Text))
        {
            return update.Text;
        }

        var textParts = update.Contents
            .OfType<Microsoft.Extensions.AI.TextContent>()
            .Select(c => c.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        return textParts.Count == 0 ? string.Empty : string.Join(string.Empty, textParts);
    }

    public void StopGeneration(string sessionId)
    {
        _logger.LogInformation("Stopping generation for session {SessionId}", sessionId);

        // 使用正确的 sessionKey 格式，与 AgentRuntime 保持一致
        var sessionKey = $"webui:{sessionId}";

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
