namespace NanoBot.Agent;

public interface IProgressReporter
{
    Task ReportProgressAsync(string content, CancellationToken cancellationToken = default);
}
