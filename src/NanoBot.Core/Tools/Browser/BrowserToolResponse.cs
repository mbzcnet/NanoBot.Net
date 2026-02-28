namespace NanoBot.Core.Tools.Browser;

public sealed class BrowserToolResponse
{
    public bool Ok { get; set; } = true;

    public string Action { get; set; } = string.Empty;

    public string Profile { get; set; } = string.Empty;

    public string? TargetId { get; set; }

    public string? Url { get; set; }

    public string? Message { get; set; }

    public string? Snapshot { get; set; }

    public string? Content { get; set; }

    public bool? Truncated { get; set; }

    public IReadOnlyList<BrowserTabInfo>? Tabs { get; set; }

    public Dictionary<string, string>? Refs { get; set; }
}
