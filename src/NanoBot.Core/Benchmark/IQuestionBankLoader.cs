namespace NanoBot.Core.Benchmark;

public interface IQuestionBankLoader
{
    Task<IReadOnlyList<BenchmarkCase>> LoadQuestionBankAsync(
        CancellationToken cancellationToken = default);
}