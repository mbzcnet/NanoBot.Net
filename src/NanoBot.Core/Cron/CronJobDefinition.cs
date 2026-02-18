namespace NanoBot.Core.Cron;

public record CronJobDefinition
{
    public required string Name { get; init; }

    public required CronSchedule Schedule { get; init; }

    public required string Message { get; init; }

    public bool Deliver { get; init; }

    public string? ChannelId { get; init; }

    public string? TargetUserId { get; init; }

    public bool DeleteAfterRun { get; init; }
}
