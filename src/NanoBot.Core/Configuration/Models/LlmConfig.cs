namespace NanoBot.Core.Configuration;

public class LlmConfig
{
    public string Model { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    public string? ApiBase { get; set; }

    public string? Provider { get; set; }

    public double Temperature { get; set; } = 0.1;

    public int MaxTokens { get; set; } = 4096;

    public string? SystemPrompt { get; set; }
}
