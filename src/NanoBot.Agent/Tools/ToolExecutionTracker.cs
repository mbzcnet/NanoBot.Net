using System.Collections.Concurrent;
using NanoBot.Agent.Messages;
using NanoBot.Core.Messages;

namespace NanoBot.Agent.Tools;

/// <summary>
/// 工具执行追踪器 - 用于在 Agent 外部追踪工具调用
/// </summary>
public interface IToolExecutionTracker
{
    /// <summary>
    /// 工具调用开始
    /// </summary>
    ToolPart OnToolInvoking(string toolName, Dictionary<string, object?> arguments, string sessionId, string messageId);

    /// <summary>
    /// 工具调用完成
    /// </summary>
    void OnToolCompleted(string callId, string output, string title);

    /// <summary>
    /// 工具调用失败
    /// </summary>
    void OnToolFailed(string callId, Exception error);

    /// <summary>
    /// 获取所有已追踪的 Part
    /// </summary>
    IEnumerable<ToolPart> GetTrackedParts();

    /// <summary>
    /// 清除追踪历史
    /// </summary>
    void Clear();
}

/// <summary>
/// 默认工具执行追踪器实现
/// </summary>
public class ToolExecutionTracker : IToolExecutionTracker
{
    private readonly ConcurrentDictionary<string, ToolPart> _trackedParts = new();
    private readonly IProgress<MessagePartUpdate>? _progress;

    public ToolExecutionTracker(IProgress<MessagePartUpdate>? progress = null)
    {
        _progress = progress;
    }

    public ToolPart OnToolInvoking(
        string toolName,
        Dictionary<string, object?> arguments,
        string sessionId,
        string messageId)
    {
        var callId = Guid.NewGuid().ToString();
        var partId = Guid.NewGuid().ToString();

        var toolPart = new ToolPart
        {
            Id = partId,
            MessageId = messageId,
            SessionId = sessionId,
            CallId = callId,
            ToolName = toolName,
            Input = arguments,
            State = new PendingToolState(),
            ExecutedAt = DateTimeOffset.UtcNow
        };

        _trackedParts[callId] = toolPart;

        // 通知进度更新
        _progress?.Report(new MessagePartUpdate
        {
            MessageId = messageId,
            PartId = partId,
            Type = UpdateType.PartAdded,
            Data = toolPart
        });

        // 更新为 Running 状态
        toolPart = toolPart with
        {
            State = new RunningToolState
            {
                Title = $"Executing {toolName}...",
                StartedAt = DateTimeOffset.UtcNow
            }
        };
        _trackedParts[callId] = toolPart;

        _progress?.Report(new MessagePartUpdate
        {
            MessageId = messageId,
            PartId = partId,
            Type = UpdateType.ToolStateChanged,
            Data = toolPart
        });

        return toolPart;
    }

    public void OnToolCompleted(string callId, string output, string title)
    {
        if (!_trackedParts.TryGetValue(callId, out var toolPart))
            return;

        var completedPart = toolPart with
        {
            State = new CompletedToolState
            {
                Output = output,
                Title = title,
                CompletedAt = DateTimeOffset.UtcNow
            }
        };

        _trackedParts[callId] = completedPart;

        _progress?.Report(new MessagePartUpdate
        {
            MessageId = toolPart.MessageId,
            PartId = toolPart.Id,
            Type = UpdateType.ToolStateChanged,
            Data = completedPart
        });
    }

    public void OnToolFailed(string callId, Exception error)
    {
        if (!_trackedParts.TryGetValue(callId, out var toolPart))
            return;

        var errorPart = toolPart with
        {
            State = new ErrorToolState
            {
                ErrorMessage = error.Message,
                StackTrace = error.StackTrace,
                CompletedAt = DateTimeOffset.UtcNow
            }
        };

        _trackedParts[callId] = errorPart;

        _progress?.Report(new MessagePartUpdate
        {
            MessageId = toolPart.MessageId,
            PartId = toolPart.Id,
            Type = UpdateType.ToolStateChanged,
            Data = errorPart
        });
    }

    public IEnumerable<ToolPart> GetTrackedParts() => _trackedParts.Values;

    public void Clear() => _trackedParts.Clear();

    /// <summary>
    /// 获取指定 callId 的 Part
    /// </summary>
    public ToolPart? GetPart(string callId) =>
        _trackedParts.TryGetValue(callId, out var part) ? part : null;

    /// <summary>
    /// 更新工具执行元数据
    /// </summary>
    public void UpdateToolMetadata(string callId, Dictionary<string, object> metadata)
    {
        if (!_trackedParts.TryGetValue(callId, out var toolPart))
            return;

        var currentState = toolPart.State;
        if (currentState is RunningToolState running)
        {
            foreach (var kvp in metadata)
            {
                running.Metadata[kvp.Key] = kvp.Value;
            }

            _progress?.Report(new MessagePartUpdate
            {
                MessageId = toolPart.MessageId,
                PartId = toolPart.Id,
                Type = UpdateType.ToolStateChanged,
                Data = toolPart
            });
        }
    }
}
