namespace NanoBot.Core.Benchmark;

public class BenchmarkResult
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Model { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;

    // 原始分数（用例分数总和）
    public int TotalScore { get; set; }
    public int MaxScore { get; set; }

    // 100分制分数
    public int ToolScore { get; set; }       // 工具得分 (0-80)
    public int VisionScore { get; set; }      // 视觉得分 (0-20)
    public int FinalScore => ToolScore + VisionScore;  // 总分 (0-100)

    // 通过统计
    public int ToolPassCount { get; set; }
    public int ToolTotalCount { get; set; }
    public int VisionPassCount { get; set; }
    public int VisionTotalCount { get; set; }

    public double ScorePercentage => MaxScore > 0 ? (double)TotalScore / MaxScore * 100 : 0;
    public bool Passed => FinalScore >= 60;

    public ModelCapabilities Capabilities { get; set; } = new();
    public List<CaseResult> CaseResults { get; set; } = new();
}

public class CaseResult
{
    public string CaseId { get; set; } = string.Empty;
    public string CaseName { get; set; } = string.Empty;
    public string Category { get; set; } = "tool";  // "tool" or "vision"
    public bool Passed { get; set; }
    public int Score { get; set; }
    public string? ErrorMessage { get; set; }

    // 请求和响应内容（用于调试日志）
    public string? RequestMessages { get; set; }
    public string? ResponseContent { get; set; }
}

public class ModelCapabilities
{
    public bool SupportsVision { get; set; }
    public bool SupportsToolCalling { get; set; }
    public DateTime? LastBenchmarkTime { get; set; }

    // 评分（100分制）
    public int Score { get; set; }
    public int ToolScore { get; set; }
    public int VisionScore { get; set; }
}