namespace NanoBot.Core.Tools.Browser;

public sealed class BrowserActionRequest
{
    public string Kind { get; set; } = string.Empty;

    public string? Ref { get; set; }

    public string? Text { get; set; }

    public string? TextGone { get; set; }

    public string? Key { get; set; }

    public int? TimeoutMs { get; set; }

    public int? ScrollBy { get; set; }

    public string? LoadState { get; set; }
}
