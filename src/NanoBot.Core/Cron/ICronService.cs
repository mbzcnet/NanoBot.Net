namespace NanoBot.Core.Cron;

public interface ICronService
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    CronJob AddJob(CronJobDefinition definition);

    bool RemoveJob(string jobId);

    CronJob? EnableJob(string jobId, bool enabled);

    Task<bool> RunJobAsync(string jobId, CancellationToken cancellationToken = default);

    IReadOnlyList<CronJob> ListJobs(bool includeDisabled = false);

    CronJob? GetJob(string jobId);

    CronServiceStatus GetStatus();

    event EventHandler<CronJobEventArgs>? JobExecuted;
}

public class CronJobEventArgs : EventArgs
{
    public required CronJob Job { get; init; }

    public required bool Success { get; init; }

    public string? Response { get; init; }

    public string? Error { get; init; }
}
