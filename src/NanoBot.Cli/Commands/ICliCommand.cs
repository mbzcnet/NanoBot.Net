namespace NanoBot.Cli.Commands;

public interface ICliCommand
{
    string Name { get; }
    string Description { get; }
    Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default);
}
