using Microsoft.Playwright;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Infrastructure.Browser;

public interface IPlaywrightSessionManager
{
    Task<bool> IsStartedAsync(string profile, CancellationToken cancellationToken = default);

    Task EnsureStartedAsync(string profile, CancellationToken cancellationToken = default);

    Task<BrowserToolResponse> StopAsync(string profile, CancellationToken cancellationToken = default);

    Task<BrowserToolResponse> StopAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BrowserTabInfo>> GetTabsAsync(string profile, CancellationToken cancellationToken = default);

    Task<(string TargetId, IPage Page)> CreatePageAsync(string profile, string? url, CancellationToken cancellationToken = default);

    Task<IPage> GetPageByTargetIdAsync(string profile, string targetId, CancellationToken cancellationToken = default);

    Task ClosePageAsync(string profile, string targetId, CancellationToken cancellationToken = default);
}
