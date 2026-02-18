namespace NanoBot.Core.Subagents;

public interface ISubagentManager
{
    Task<SubagentResult> SpawnAsync(
        string task,
        string? label,
        string originChannel,
        string originChatId,
        CancellationToken cancellationToken = default);

    IReadOnlyList<SubagentInfo> GetActiveSubagents();

    SubagentInfo? GetSubagent(string id);

    bool Cancel(string id);

    event EventHandler<SubagentCompletedEventArgs>? SubagentCompleted;
}
