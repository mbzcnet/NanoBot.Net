namespace NanoBot.Core.Subagents;

public class SubagentCompletedEventArgs : EventArgs
{
    public required SubagentResult Result { get; init; }

    public required string OriginChannel { get; init; }

    public required string OriginChatId { get; init; }
}
