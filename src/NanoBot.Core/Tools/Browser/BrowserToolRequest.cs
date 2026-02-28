namespace NanoBot.Core.Tools.Browser;

public sealed class BrowserToolRequest
{
    public string Action { get; set; } = string.Empty;

    public string Profile { get; set; } = "openclaw";

    public string? TargetId { get; set; }

    public string? TargetUrl { get; set; }

    public string SnapshotFormat { get; set; } = "ai";

    public BrowserActionRequest? Request { get; set; }
}
