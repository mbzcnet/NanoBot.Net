namespace NanoBot.Core.Cron;

public record CronServiceStatus
{
    public bool Running { get; init; }

    public int TotalJobs { get; init; }

    public int EnabledJobs { get; init; }

    public DateTimeOffset? NextWakeAt { get; init; }
}
