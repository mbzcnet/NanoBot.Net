namespace NanoBot.Core.Cron;

public enum CronScheduleKind
{
    At,
    Every,
    Cron
}

public record CronSchedule
{
    public required CronScheduleKind Kind { get; init; }

    public long? AtMs { get; init; }

    public long? EveryMs { get; init; }

    public string? Expression { get; init; }

    public string? TimeZone { get; init; }
}
