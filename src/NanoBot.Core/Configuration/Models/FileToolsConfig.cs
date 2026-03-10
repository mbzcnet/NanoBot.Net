namespace NanoBot.Core.Configuration;

/// <summary>
/// 文件工具配置
/// </summary>
public class FileToolsConfig
{
    /// <summary>
    /// 是否使用增强文件工具
    /// </summary>
    public bool UseEnhanced { get; set; } = false;

    /// <summary>
    /// 读取配置
    /// </summary>
    public FileReadConfig Read { get; set; } = new();

    /// <summary>
    /// 编辑配置
    /// </summary>
    public FileEditConfig Edit { get; set; } = new();
}

/// <summary>
/// 文件读取配置
/// </summary>
public class FileReadConfig
{
    /// <summary>
    /// 最大读取字符数（默认 128KB）
    /// </summary>
    public int MaxChars { get; set; } = 128_000;

    /// <summary>
    /// 单次最大字节数（默认 50KB）
    /// </summary>
    public int MaxBytes { get; set; } = 50 * 1024;

    /// <summary>
    /// 单行最大长度（默认 2000）
    /// </summary>
    public int MaxLineLength { get; set; } = 2000;

    /// <summary>
    /// 默认读取行数限制
    /// </summary>
    public int DefaultLineLimit { get; set; } = 2000;

    /// <summary>
    /// 是否启用二进制文件检测
    /// </summary>
    public bool EnableBinaryDetection { get; set; } = true;
}

/// <summary>
/// 文件编辑配置
/// </summary>
public class FileEditConfig
{
    /// <summary>
    /// 单候选相似度阈值（默认 0.0，即只要有锚点匹配就接受）
    /// </summary>
    public double SingleCandidateThreshold { get; set; } = 0.0;

    /// <summary>
    /// 多候选相似度阈值（默认 0.3）
    /// </summary>
    public double MultipleCandidatesThreshold { get; set; } = 0.3;

    /// <summary>
    /// 是否启用模糊匹配建议
    /// </summary>
    public bool EnableFuzzySuggestions { get; set; } = true;
}
