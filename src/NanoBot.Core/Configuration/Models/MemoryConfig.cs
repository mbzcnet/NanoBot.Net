namespace NanoBot.Core.Configuration;

public class MemoryConfig
{
    public string MemoryFile { get; set; } = "MEMORY.md";

    public string HistoryFile { get; set; } = "HISTORY.md";

    public int MaxHistoryEntries { get; set; } = 500;

    public bool Enabled { get; set; } = true;
}
