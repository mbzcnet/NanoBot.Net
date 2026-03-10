namespace NanoBot.Core.Messages;

/// <summary>
/// 文件附件 Part
/// </summary>
public record FilePart : MessagePart
{
    public override string Type => "file";

    /// <summary>
    /// 文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 文件内容（可选，小文件可直接嵌入）
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long? Size { get; init; }

    /// <summary>
    /// MIME 类型
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string? FileName => Path.GetFileName(FilePath);
}
