namespace NanoBot.Core.Cron;

public record CronJob
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required CronSchedule Schedule { get; init; }

    public required string Message { get; init; }

    public bool Enabled { get; set; } = true;

    public bool Deliver { get; init; }

    public string? ChannelId { get; init; }

    public string? TargetUserId { get; init; }

    public bool DeleteAfterRun { get; init; }

    public CronJobState State { get; init; } = new();

    public long CreatedAtMs { get; init; }

    public long UpdatedAtMs { get; set; }

    public DateTimeOffset? LastRunAt => State.LastRunAtMs.HasValue
        ? DateTimeOffset.FromUnixTimeMilliseconds(State.LastRunAtMs.Value)
        : null;

    public DateTimeOffset? NextRunAt => State.NextRunAtMs.HasValue
        ? DateTimeOffset.FromUnixTimeMilliseconds(State.NextRunAtMs.Value)
        : null;
}
