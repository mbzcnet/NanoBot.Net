namespace NanoBot.Core.Sessions;

public record ToolCallInfo(
    string Name,
    string? Arguments,
    string? CallId = null
);

public interface IAgentService
{
    Task<string> SendMessageAsync(string sessionId, string message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AgentResponseChunk> SendMessageStreamingAsync(string sessionId, string message, CancellationToken cancellationToken = default);
    void StopGeneration(string sessionId);
}

public record AgentResponseChunk(
    string Content,
    bool IsComplete,
    string? ToolCall = null,
    ToolCallInfo? ToolCallDetails = null
);
