namespace NanoBot.Core.Benchmark;

public class BenchmarkCase
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "tool";  // "tool" or "vision"
    public string Input { get; set; } = string.Empty;
    public List<string> RequiredTools { get; set; } = new();
    public int Score { get; set; } = 10;
    public string? ImagePath { get; set; }  // For vision tests

    // Tool validation: keywords that should appear in output
    public List<string> Keywords { get; set; } = new();

    // Vision validation: expected content in response
    public List<string> ExpectedContent { get; set; } = new();
}