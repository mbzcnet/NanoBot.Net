using System.CommandLine;

namespace NanoBot.Cli.Commands;

public interface ICliCommand
{
    string Name { get; }
    string Description { get; }
    Command CreateCommand();
}
