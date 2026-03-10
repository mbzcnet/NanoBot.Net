namespace NanoBot.Tools.BuiltIn.Filesystem.Enhanced.Models;

/// <summary>
/// 匹配结果
/// </summary>
public readonly record struct MatchResult
{
    /// <summary>
    /// 匹配的文本
    /// </summary>
    public string MatchedText { get; init; }

    /// <summary>
    /// 起始索引
    /// </summary>
    public int StartIndex { get; init; }

    /// <summary>
    /// 匹配长度
    /// </summary>
    public int Length { get; init; }

    /// <summary>
    /// 相似度 (0.0 - 1.0)
    /// </summary>
    public double Similarity { get; init; }

    /// <summary>
    /// 使用的替换器名称
    /// </summary>
    public string ReplacerName { get; init; }

    public MatchResult(string matchedText, int startIndex, int length, double similarity = 1.0, string? replacerName = null)
    {
        MatchedText = matchedText;
        StartIndex = startIndex;
        Length = length;
        Similarity = similarity;
        ReplacerName = replacerName ?? string.Empty;
    }
}
