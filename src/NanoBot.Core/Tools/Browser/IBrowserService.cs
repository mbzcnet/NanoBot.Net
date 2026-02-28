namespace NanoBot.Core.Tools.Browser;

public interface IBrowserService
{
    Task<BrowserToolResponse> GetStatusAsync(string profile, CancellationToken cancellationToken = default);

    Task<BrowserToolResponse> StartAsync(string profile, CancellationToken cancellationToken = default);

    Task<BrowserToolResponse> StopAsync(string profile, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BrowserTabInfo>> GetTabsAsync(string profile, CancellationToken cancellationToken = default);

    Task<BrowserToolResponse> OpenTabAsync(string url, string profile, CancellationToken cancellationToken = default);

    Task<BrowserToolResponse> NavigateAsync(string targetId, string url, string profile, CancellationToken cancellationToken = default);

    Task<BrowserToolResponse> CloseTabAsync(string targetId, string profile, CancellationToken cancellationToken = default);

    Task<BrowserToolResponse> GetSnapshotAsync(string targetId, string format, string profile, CancellationToken cancellationToken = default);

    Task<BrowserToolResponse> GetContentAsync(string targetId, string? selector, int? maxChars, string profile, CancellationToken cancellationToken = default);

    Task<BrowserToolResponse> ExecuteActionAsync(BrowserActionRequest request, string targetId, string profile, CancellationToken cancellationToken = default);
}
