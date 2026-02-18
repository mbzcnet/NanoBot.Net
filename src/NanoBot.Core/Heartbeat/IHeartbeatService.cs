namespace NanoBot.Core.Heartbeat;

public interface IHeartbeatService
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    HeartbeatJob AddJob(HeartbeatDefinition definition);

    bool RemoveJob(string jobId);

    IReadOnlyList<HeartbeatJob> ListJobs();

    HeartbeatStatus GetStatus();

    Task<string?> TriggerNowAsync();

    event EventHandler<HeartbeatEventArgs>? HeartbeatExecuted;
}

public class HeartbeatEventArgs : EventArgs
{
    public required string Prompt { get; init; }

    public required string Response { get; init; }

    public bool Success { get; init; }

    public string? Error { get; init; }
}
