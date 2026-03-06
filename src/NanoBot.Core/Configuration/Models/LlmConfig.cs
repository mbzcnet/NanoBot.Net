namespace NanoBot.Core.Configuration;

public class LlmProfile
{
    /// <summary>
    /// 显示名称，用于UI展示。为空时默认使用 "{Provider}-{Model}"
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string? ApiBase { get; set; }

    public string? Provider { get; set; }

    public double Temperature { get; set; } = 0.1;

    public int MaxTokens { get; set; } = 4096;

    public string? SystemPrompt { get; set; }

    /// <summary>
    /// 获取显示名称，优先使用 Name 属性，为空则返回 "{Provider}-{Model}"
    /// </summary>
    public string GetDisplayName() =>
        string.IsNullOrWhiteSpace(Name) ? $"{Provider}-{Model}" : Name;
}

public class LlmConfig
{
    public Dictionary<string, LlmProfile> Profiles { get; set; } = new()
    {
        ["default"] = new LlmProfile()
    };

    public string DefaultProfile { get; set; } = "default";
}
