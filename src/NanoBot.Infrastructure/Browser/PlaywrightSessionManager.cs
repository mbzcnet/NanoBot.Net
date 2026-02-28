using System.Collections.Concurrent;
using Microsoft.Playwright;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Infrastructure.Browser;

public sealed class PlaywrightSessionManager : IPlaywrightSessionManager, IAsyncDisposable
{
    private sealed class ProfileState
    {
        public required string Profile { get; init; }
        public required IBrowser Browser { get; init; }
        public required IBrowserContext Context { get; init; }
        public required ConcurrentDictionary<string, IPage> Pages { get; init; }
        public int NextTargetId;
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, ProfileState> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private IPlaywright? _playwright;

    public async Task<bool> IsStartedAsync(string profile, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return _profiles.ContainsKey(NormalizeProfile(profile));
    }

    public async Task EnsureStartedAsync(string profile, CancellationToken cancellationToken = default)
    {
        var normalizedProfile = NormalizeProfile(profile);
        if (_profiles.ContainsKey(normalizedProfile))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_profiles.ContainsKey(normalizedProfile))
            {
                return;
            }

            _playwright ??= await Playwright.CreateAsync();
            var browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });
            var context = await browser.NewContextAsync();
            var state = new ProfileState
            {
                Profile = normalizedProfile,
                Browser = browser,
                Context = context,
                Pages = new ConcurrentDictionary<string, IPage>(StringComparer.Ordinal),
                NextTargetId = 0
            };

            _profiles[normalizedProfile] = state;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrowserToolResponse> StopAsync(string profile, CancellationToken cancellationToken = default)
    {
        var normalizedProfile = NormalizeProfile(profile);
        if (!_profiles.TryRemove(normalizedProfile, out var state))
        {
            return new BrowserToolResponse
            {
                Ok = true,
                Action = "stop",
                Profile = normalizedProfile,
                Message = "Browser not started"
            };
        }

        await state.Context.CloseAsync();
        await state.Browser.CloseAsync();

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "stop",
            Profile = normalizedProfile,
            Message = "Browser stopped"
        };
    }

    public async Task<BrowserToolResponse> StopAllAsync(CancellationToken cancellationToken = default)
    {
        var profiles = _profiles.Keys.ToArray();
        foreach (var profile in profiles)
        {
            await StopAsync(profile, cancellationToken);
        }

        if (_playwright != null)
        {
            _playwright.Dispose();
            _playwright = null;
        }

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "stop",
            Profile = "all",
            Message = "All browser profiles stopped"
        };
    }

    public async Task<IReadOnlyList<BrowserTabInfo>> GetTabsAsync(string profile, CancellationToken cancellationToken = default)
    {
        var state = await GetOrStartProfileAsync(profile, cancellationToken);
        RefreshClosedPages(state);

        var tabs = new List<BrowserTabInfo>();
        foreach (var entry in state.Pages)
        {
            var title = string.Empty;
            try
            {
                title = await entry.Value.TitleAsync();
            }
            catch
            {
            }

            tabs.Add(new BrowserTabInfo
            {
                TargetId = entry.Key,
                Url = entry.Value.Url,
                Title = title
            });
        }

        return tabs;
    }

    public async Task<(string TargetId, IPage Page)> CreatePageAsync(string profile, string? url, CancellationToken cancellationToken = default)
    {
        var state = await GetOrStartProfileAsync(profile, cancellationToken);
        var page = await state.Context.NewPageAsync();
        var targetId = $"t{Interlocked.Increment(ref state.NextTargetId)}";
        state.Pages[targetId] = page;

        page.Close += (_, _) =>
        {
            state.Pages.TryRemove(targetId, out _);
        };

        if (!string.IsNullOrWhiteSpace(url))
        {
            await page.GotoAsync(url);
        }

        return (targetId, page);
    }

    public async Task<IPage> GetPageByTargetIdAsync(string profile, string targetId, CancellationToken cancellationToken = default)
    {
        var state = await GetOrStartProfileAsync(profile, cancellationToken);
        RefreshClosedPages(state);

        if (state.Pages.TryGetValue(targetId, out var page))
        {
            return page;
        }

        throw new InvalidOperationException($"Tab not found: {targetId}");
    }

    public async Task ClosePageAsync(string profile, string targetId, CancellationToken cancellationToken = default)
    {
        var page = await GetPageByTargetIdAsync(profile, targetId, cancellationToken);
        await page.CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllAsync();
        _gate.Dispose();
    }

    private async Task<ProfileState> GetOrStartProfileAsync(string profile, CancellationToken cancellationToken)
    {
        var normalizedProfile = NormalizeProfile(profile);
        await EnsureStartedAsync(normalizedProfile, cancellationToken);

        if (_profiles.TryGetValue(normalizedProfile, out var state))
        {
            return state;
        }

        throw new InvalidOperationException($"Browser profile unavailable: {normalizedProfile}");
    }

    private static string NormalizeProfile(string profile)
    {
        return string.IsNullOrWhiteSpace(profile) ? "openclaw" : profile.Trim();
    }

    private static void RefreshClosedPages(ProfileState state)
    {
        foreach (var entry in state.Pages)
        {
            if (entry.Value.IsClosed)
            {
                state.Pages.TryRemove(entry.Key, out _);
            }
        }
    }

}
