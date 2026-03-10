namespace NanoBot.Tools.BuiltIn.Filesystem.Enhanced.Models;

/// <summary>
/// 文件读取结果
/// </summary>
public record ReadResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 读取的行列表
    /// </summary>
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 文件总行数
    /// </summary>
    public int TotalLines { get; init; }

    /// <summary>
    /// 是否有更多内容
    /// </summary>
    public bool HasMore { get; init; }

    /// <summary>
    /// 起始行号（1-based）
    /// </summary>
    public int StartLine { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? Error { get; init; }

    public static ReadResult SuccessResult(IReadOnlyList<string> lines, int totalLines, bool hasMore, int startLine)
        => new()
        {
            Success = true,
            Lines = lines,
            TotalLines = totalLines,
            HasMore = hasMore,
            StartLine = startLine
        };

    public static ReadResult ErrorResult(string error)
        => new()
        {
            Success = false,
            Error = error
        };
}
