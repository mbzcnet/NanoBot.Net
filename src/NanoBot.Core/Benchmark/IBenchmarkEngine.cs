using Microsoft.Extensions.AI;

namespace NanoBot.Core.Benchmark;

/// <summary>
/// 评测引擎接口
/// </summary>
public interface IBenchmarkEngine
{
    /// <summary>
    /// 运行评测
    /// </summary>
    Task<BenchmarkResult> RunBenchmarkAsync(
        IChatClient chatClient,
        IReadOnlyList<AITool> tools,
        CancellationToken cancellationToken = default);
}