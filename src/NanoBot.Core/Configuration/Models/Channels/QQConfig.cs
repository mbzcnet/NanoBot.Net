namespace NanoBot.Core.Configuration;

public class QQConfig
{
    public bool Enabled { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public IReadOnlyList<string> AllowFrom { get; set; } = Array.Empty<string>();
}
