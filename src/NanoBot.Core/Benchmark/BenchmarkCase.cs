namespace NanoBot.Core.Benchmark;

public class BenchmarkCase
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public List<string> RequiredTools { get; set; } = new();
    public int Score { get; set; } = 10;
    public string? ImagePath { get; set; }
}