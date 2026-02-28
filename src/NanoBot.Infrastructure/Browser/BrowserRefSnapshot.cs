namespace NanoBot.Infrastructure.Browser;

internal sealed class BrowserRefSnapshot
{
    public string TargetId { get; init; } = string.Empty;

    public Dictionary<string, string> Refs { get; init; } = new(StringComparer.Ordinal);

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
