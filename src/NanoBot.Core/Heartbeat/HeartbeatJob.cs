namespace NanoBot.Core.Heartbeat;

using NanoBot.Core.Jobs;

/// <summary>
/// Represents a heartbeat job that runs at regular intervals.
/// </summary>
public record HeartbeatJob : ScheduledJob
{
    /// <summary>
    /// Interval in seconds between heartbeats.
    /// </summary>
    public required int IntervalSeconds { get; init; }

    /// <summary>
    /// Target chat ID for delivery.
    /// </summary>
    public string? ChatId { get; init; }

    /// <summary>
    /// When the heartbeat last ran.
    /// </summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>
    /// When the next heartbeat is scheduled.
    /// </summary>
    public DateTimeOffset? NextRunAt { get; set; }
}
