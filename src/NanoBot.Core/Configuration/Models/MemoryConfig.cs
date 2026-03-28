namespace NanoBot.Core.Configuration;

public class MemoryConfig
{
    public string MemoryFile { get; set; } = "MEMORY.md";

    public int MaxHistoryEntries { get; set; } = 500;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Enable HISTORY.md for grep-searchable conversation archive.
    /// </summary>
    public bool EnableHistory { get; set; } = true;

    public int MemoryWindow { get; set; } = 100;

    /// <summary>
    /// Maximum total characters for system instructions (context providers output).
    /// When exceeded, context is truncated with a warning. 0 = unlimited.
    /// Reduce this for local models (e.g. Ollama) to improve response latency.
    /// </summary>
    public int MaxInstructionChars { get; set; } = 0;
}
