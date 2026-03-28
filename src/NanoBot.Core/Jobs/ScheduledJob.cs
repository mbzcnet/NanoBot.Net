namespace NanoBot.Core.Jobs;

/// <summary>
/// Base class for scheduled job types.
/// </summary>
public abstract record ScheduledJob
{
    /// <summary>
    /// Unique identifier for the job.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name for the job.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The message or prompt to execute.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Whether the job is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Target channel ID for delivery.
    /// </summary>
    public string? ChannelId { get; init; }
}
