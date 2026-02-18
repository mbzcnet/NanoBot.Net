namespace NanoBot.Core.Configuration;

public class FeishuConfig
{
    public bool Enabled { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string EncryptKey { get; set; } = string.Empty;
    public string VerificationToken { get; set; } = string.Empty;
    public IReadOnlyList<string> AllowFrom { get; set; } = Array.Empty<string>();
}
