namespace NanoBot.Core.Cron;

using NanoBot.Core.Jobs;

/// <summary>
/// Represents a cron-scheduled job.
/// </summary>
public record CronJob : ScheduledJob
{
    /// <summary>
    /// The cron schedule expression.
    /// </summary>
    public required CronSchedule Schedule { get; init; }

    /// <summary>
    /// Whether to deliver results to the channel.
    /// </summary>
    public bool Deliver { get; init; }

    /// <summary>
    /// Target user ID for direct message delivery.
    /// </summary>
    public string? TargetUserId { get; init; }

    /// <summary>
    /// Whether to delete the job after running.
    /// </summary>
    public bool DeleteAfterRun { get; init; }

    /// <summary>
    /// Current job state.
    /// </summary>
    public CronJobState State { get; init; } = new();

    /// <summary>
    /// Timestamp when the job was created (Unix ms).
    /// </summary>
    public long CreatedAtMs { get; init; }

    /// <summary>
    /// Timestamp when the job was last updated (Unix ms).
    /// </summary>
    public long UpdatedAtMs { get; set; }

    /// <summary>
    /// When the job last ran.
    /// </summary>
    public DateTimeOffset? LastRunAt => State.LastRunAtMs.HasValue
        ? DateTimeOffset.FromUnixTimeMilliseconds(State.LastRunAtMs.Value)
        : null;

    /// <summary>
    /// When the job will run next.
    /// </summary>
    public DateTimeOffset? NextRunAt => State.NextRunAtMs.HasValue
        ? DateTimeOffset.FromUnixTimeMilliseconds(State.NextRunAtMs.Value)
        : null;
}
