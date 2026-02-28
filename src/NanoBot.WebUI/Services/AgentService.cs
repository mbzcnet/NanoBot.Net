using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NanoBot.Agent;

namespace NanoBot.WebUI.Services;

public class AgentService : IAgentService
{
    private readonly IAgentRuntime _agentRuntime;
    private readonly ILogger<AgentService> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeSessions;

    public AgentService(
        IAgentRuntime agentRuntime,
        ILogger<AgentService> logger)
    {
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
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
            yield return new AgentResponseChunk(
                Content: $"\n\n[错误: {error.Message}]",
                IsComplete: true
            );
            _activeSessions.TryRemove(sessionId, out _);
            cts.Dispose();
            yield break;
        }
        
        if (stream != null)
        {
            var cancelled = false;
            await foreach (var update in stream.WithCancellation(cts.Token))
            {
                if (cts.Token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                
                var text = update.Text ?? string.Empty;
                var toolCall = update.Contents.OfType<Microsoft.Extensions.AI.FunctionCallContent>().FirstOrDefault()?.Name;
                
                yield return new AgentResponseChunk(
                    Content: text,
                    IsComplete: false,
                    ToolCall: toolCall
                );
            }
            
            if (cancelled)
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

    public void StopGeneration(string sessionId)
    {
        _logger.LogInformation("Stopping generation for session {SessionId}", sessionId);
        
        if (_activeSessions.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
