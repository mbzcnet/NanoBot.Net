namespace NanoBot.Core.Cron;

public record CronJobState
{
    public long? NextRunAtMs { get; set; }

    public long? LastRunAtMs { get; set; }

    public string? LastStatus { get; set; }

    public string? LastError { get; set; }
}
