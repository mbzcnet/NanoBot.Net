namespace NanoBot.Core.Benchmark;

public class BenchmarkResult
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Model { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public int TotalScore { get; set; }
    public int MaxScore { get; set; }
    public double ScorePercentage => MaxScore > 0 ? (double)TotalScore / MaxScore * 100 : 0;
    public bool Passed => ScorePercentage >= 60;
    public ModelCapabilities Capabilities { get; set; } = new();
    public List<CaseResult> CaseResults { get; set; } = new();
}

public class CaseResult
{
    public string CaseId { get; set; } = string.Empty;
    public string CaseName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public int Score { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ActualTools { get; set; }
}

public class ModelCapabilities
{
    public bool SupportsVision { get; set; }
    public bool SupportsToolCalling { get; set; }
    public DateTime? LastBenchmarkTime { get; set; }
}