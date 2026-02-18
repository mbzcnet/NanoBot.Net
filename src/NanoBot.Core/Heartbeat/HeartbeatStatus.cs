namespace NanoBot.Core.Heartbeat;

public record HeartbeatStatus
{
    public bool Running { get; init; }

    public int ActiveJobs { get; init; }
}
