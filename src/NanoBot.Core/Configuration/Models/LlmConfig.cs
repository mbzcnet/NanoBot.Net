namespace NanoBot.Core.Configuration;

public class LlmProfile
{
    public string Name { get; set; } = "default";

    public string Model { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string? ApiBase { get; set; }

    public string? Provider { get; set; }

    public double Temperature { get; set; } = 0.1;

    public int MaxTokens { get; set; } = 4096;

    public string? SystemPrompt { get; set; }
}

public class LlmConfig
{
    public Dictionary<string, LlmProfile> Profiles { get; set; } = new()
    {
        ["default"] = new LlmProfile()
    };

    public string DefaultProfile { get; set; } = "default";
}
