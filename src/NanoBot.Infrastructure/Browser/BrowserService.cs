using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Infrastructure.Browser;

public sealed class BrowserService : IBrowserService
{
    private const int DefaultContentMaxChars = 8000;

    private readonly IPlaywrightSessionManager _sessionManager;
    private readonly ConcurrentDictionary<string, BrowserRefSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    public BrowserService(IPlaywrightSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public async Task<BrowserToolResponse> GetStatusAsync(string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        var started = await _sessionManager.IsStartedAsync(normalized, cancellationToken);
        var tabs = started ? await _sessionManager.GetTabsAsync(normalized, cancellationToken) : [];

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "status",
            Profile = normalized,
            Message = started ? "Browser is running" : "Browser is stopped",
            Tabs = tabs
        };
    }

    public async Task<BrowserToolResponse> StartAsync(string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        await _sessionManager.EnsureStartedAsync(normalized, cancellationToken);

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "start",
            Profile = normalized,
            Message = "Browser started"
        };
    }

    public Task<BrowserToolResponse> StopAsync(string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        CleanupSnapshotsForProfile(normalized);
        return _sessionManager.StopAsync(normalized, cancellationToken);
    }

    public Task<IReadOnlyList<BrowserTabInfo>> GetTabsAsync(string profile, CancellationToken cancellationToken = default)
    {
        return _sessionManager.GetTabsAsync(NormalizeProfile(profile), cancellationToken);
    }

    public async Task<BrowserToolResponse> OpenTabAsync(string url, string profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("targetUrl is required", nameof(url));
        }

        var normalized = NormalizeProfile(profile);
        var (targetId, page) = await _sessionManager.CreatePageAsync(normalized, url, cancellationToken);

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "open",
            Profile = normalized,
            TargetId = targetId,
            Url = page.Url,
            Message = "Tab opened"
        };
    }

    public async Task<BrowserToolResponse> NavigateAsync(string targetId, string url, string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        var page = await _sessionManager.GetPageByTargetIdAsync(normalized, targetId, cancellationToken);

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("targetUrl is required", nameof(url));
        }

        await page.GotoAsync(url);
        _snapshots.TryRemove(SnapshotKey(normalized, targetId), out _);

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "navigate",
            Profile = normalized,
            TargetId = targetId,
            Url = page.Url,
            Message = "Navigation completed"
        };
    }

    public async Task<BrowserToolResponse> CloseTabAsync(string targetId, string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        await _sessionManager.ClosePageAsync(normalized, targetId, cancellationToken);
        _snapshots.TryRemove(SnapshotKey(normalized, targetId), out _);

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "close",
            Profile = normalized,
            TargetId = targetId,
            Message = "Tab closed"
        };
    }

    public async Task<BrowserToolResponse> GetSnapshotAsync(string targetId, string format, string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        var page = await _sessionManager.GetPageByTargetIdAsync(normalized, targetId, cancellationToken);

        var nodesJson = await page.EvaluateAsync<string>(@"() => JSON.stringify((() => {
            const elements = Array.from(document.querySelectorAll('a, button, input, textarea, select, [role], [tabindex]'));
            const visible = elements.filter((el) => {
                if (!(el instanceof HTMLElement)) return false;
                const rect = el.getBoundingClientRect();
                const style = window.getComputedStyle(el);
                return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
            }).slice(0, 150);

            return visible.map((el, index) => {
                const ref = String(index + 1);
                el.setAttribute('data-nbot-ref', ref);
                const tag = el.tagName.toLowerCase();
                const role = el.getAttribute('role') || '';
                const text = (el.innerText || el.getAttribute('aria-label') || el.getAttribute('value') || '').trim().replace(/\s+/g, ' ');
                return {
                    ref,
                    role,
                    tag,
                    text: text.slice(0, 120)
                };
            });
        })())") ?? "[]";

        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = new List<string>();

        using var nodesDoc = JsonDocument.Parse(nodesJson);
        foreach (var node in nodesDoc.RootElement.EnumerateArray())
        {
            if (!node.TryGetProperty("ref", out var refElement))
            {
                continue;
            }

            var nodeRef = refElement.GetString();
            if (string.IsNullOrWhiteSpace(nodeRef))
            {
                continue;
            }

            refs[nodeRef] = $"[data-nbot-ref=\"{nodeRef}\"]";

            var nodeRole = node.TryGetProperty("role", out var roleElement)
                ? roleElement.GetString()
                : null;
            var nodeTag = node.TryGetProperty("tag", out var tagElement)
                ? tagElement.GetString()
                : null;
            var nodeText = node.TryGetProperty("text", out var textElement)
                ? textElement.GetString()
                : null;

            var rolePart = string.IsNullOrWhiteSpace(nodeRole) ? (nodeTag ?? "element") : nodeRole;
            var textPart = string.IsNullOrWhiteSpace(nodeText) ? "(no text)" : nodeText;
            lines.Add($"[{nodeRef}] {rolePart}: {textPart}");
        }

        var snapshotText = lines.Count == 0
            ? "No interactive elements found."
            : string.Join("\n", lines);

        _snapshots[SnapshotKey(normalized, targetId)] = new BrowserRefSnapshot
        {
            TargetId = targetId,
            Refs = refs,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "snapshot",
            Profile = normalized,
            TargetId = targetId,
            Url = page.Url,
            Snapshot = snapshotText,
            Refs = refs,
            Message = string.Equals(format, "aria", StringComparison.OrdinalIgnoreCase)
                ? "ARIA format fallback: returned simplified interactive snapshot"
                : "Snapshot captured"
        };
    }

    public async Task<BrowserToolResponse> GetContentAsync(string targetId, string? selector, int? maxChars, string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        var page = await _sessionManager.GetPageByTargetIdAsync(normalized, targetId, cancellationToken);
        var selectorValue = string.IsNullOrWhiteSpace(selector) ? null : selector.Trim();

        var rawContent = await page.EvaluateAsync<string>(@"(selector) => {
            const readText = (el) => {
                if (!el) return '';
                const text = (el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim();
                return text;
            };

            let root = null;
            if (selector) {
                try {
                    root = document.querySelector(selector);
                } catch {
                    root = null;
                }
            }

            if (!root) {
                root = document.querySelector('main article')
                    || document.querySelector('article')
                    || document.querySelector('main')
                    || document.body;
            }

            return readText(root);
        }", selectorValue) ?? string.Empty;

        var limit = maxChars.GetValueOrDefault(DefaultContentMaxChars);
        if (limit <= 0)
        {
            limit = DefaultContentMaxChars;
        }

        var truncated = rawContent.Length > limit;
        var content = truncated ? rawContent[..limit] : rawContent;

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "content",
            Profile = normalized,
            TargetId = targetId,
            Url = page.Url,
            Content = content,
            Truncated = truncated,
            Message = string.IsNullOrWhiteSpace(content)
                ? "No readable content found"
                : "Content extracted"
        };
    }

    public async Task<BrowserToolResponse> ExecuteActionAsync(BrowserActionRequest request, string targetId, string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        var page = await _sessionManager.GetPageByTargetIdAsync(normalized, targetId, cancellationToken);

        var kind = request.Kind?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("request.kind is required", nameof(request));
        }

        switch (kind)
        {
            case "click":
            {
                if (string.IsNullOrWhiteSpace(request.Ref))
                {
                    throw new InvalidOperationException("request.ref is required for click/type");
                }

                var selector = ResolveRefSelector(normalized, targetId, request.Ref);
                var locator = page.Locator(selector).First;
                await locator.ClickAsync();

                return new BrowserToolResponse
                {
                    Ok = true,
                    Action = "act",
                    Profile = normalized,
                    TargetId = targetId,
                    Url = page.Url,
                    Message = "Action executed: click"
                };
            }
            case "type":
            {
                if (string.IsNullOrWhiteSpace(request.Ref))
                {
                    throw new InvalidOperationException("request.ref is required for click/type");
                }

                if (request.Text == null)
                {
                    throw new InvalidOperationException("request.text is required for type");
                }

                var selector = ResolveRefSelector(normalized, targetId, request.Ref);
                var locator = page.Locator(selector).First;
                await locator.FillAsync(request.Text);

                return new BrowserToolResponse
                {
                    Ok = true,
                    Action = "act",
                    Profile = normalized,
                    TargetId = targetId,
                    Url = page.Url,
                    Message = "Action executed: type"
                };
            }
            case "press":
            {
                var key = request.Key?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("request.key is required for press");
                }

                if (!string.IsNullOrWhiteSpace(request.Ref))
                {
                    var selector = ResolveRefSelector(normalized, targetId, request.Ref);
                    await page.Locator(selector).First.PressAsync(key);
                }
                else
                {
                    await page.Keyboard.PressAsync(key);
                }

                return new BrowserToolResponse
                {
                    Ok = true,
                    Action = "act",
                    Profile = normalized,
                    TargetId = targetId,
                    Url = page.Url,
                    Message = "Action executed: press"
                };
            }
            case "hover":
            {
                if (string.IsNullOrWhiteSpace(request.Ref))
                {
                    throw new InvalidOperationException("request.ref is required for hover");
                }

                var selector = ResolveRefSelector(normalized, targetId, request.Ref);
                var locator = page.Locator(selector).First;
                var hoverOptions = request.TimeoutMs.HasValue
                    ? new LocatorHoverOptions { Timeout = request.TimeoutMs.Value }
                    : null;
                await locator.HoverAsync(hoverOptions);

                return new BrowserToolResponse
                {
                    Ok = true,
                    Action = "act",
                    Profile = normalized,
                    TargetId = targetId,
                    Url = page.Url,
                    Message = "Action executed: hover"
                };
            }
            case "wait":
            {
                var timeoutMs = request.TimeoutMs.GetValueOrDefault(3000);
                if (timeoutMs <= 0)
                {
                    throw new InvalidOperationException("request.timeoutMs must be greater than 0 for wait");
                }

                var hasCondition = false;
                if (!string.IsNullOrWhiteSpace(request.LoadState))
                {
                    var state = ParseLoadState(request.LoadState);
                    await page.WaitForLoadStateAsync(state, new PageWaitForLoadStateOptions
                    {
                        Timeout = timeoutMs
                    });
                    hasCondition = true;
                }

                if (!string.IsNullOrWhiteSpace(request.Text))
                {
                    await page.GetByText(request.Text.Trim()).First.WaitForAsync(new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Visible,
                        Timeout = timeoutMs
                    });
                    hasCondition = true;
                }

                if (!string.IsNullOrWhiteSpace(request.TextGone))
                {
                    await page.GetByText(request.TextGone.Trim()).First.WaitForAsync(new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Hidden,
                        Timeout = timeoutMs
                    });
                    hasCondition = true;
                }

                if (!hasCondition)
                {
                    await Task.Delay(timeoutMs, cancellationToken);
                }

                return new BrowserToolResponse
                {
                    Ok = true,
                    Action = "act",
                    Profile = normalized,
                    TargetId = targetId,
                    Url = page.Url,
                    Message = !hasCondition
                        ? $"Action executed: wait({timeoutMs}ms)"
                        : "Action executed: wait(condition)"
                };
            }
            case "scroll":
            {
                var scrollBy = request.ScrollBy.GetValueOrDefault(800);
                if (scrollBy == 0)
                {
                    throw new InvalidOperationException("request.scrollBy must not be 0 for scroll");
                }

                await page.Mouse.WheelAsync(0, scrollBy);

                return new BrowserToolResponse
                {
                    Ok = true,
                    Action = "act",
                    Profile = normalized,
                    TargetId = targetId,
                    Url = page.Url,
                    Message = $"Action executed: scroll({scrollBy})"
                };
            }
            default:
                throw new InvalidOperationException($"Unsupported action kind: {kind}");
        }
    }

    private static string NormalizeProfile(string profile)
    {
        return string.IsNullOrWhiteSpace(profile) ? "openclaw" : profile.Trim();
    }

    private static string SnapshotKey(string profile, string targetId)
    {
        return $"{profile}::{targetId}";
    }

    private static LoadState ParseLoadState(string loadState)
    {
        return loadState.Trim().ToLowerInvariant() switch
        {
            "load" => LoadState.Load,
            "domcontentloaded" => LoadState.DOMContentLoaded,
            "networkidle" => LoadState.NetworkIdle,
            _ => throw new InvalidOperationException("request.loadState must be one of: load, domcontentloaded, networkidle")
        };
    }

    private string ResolveRefSelector(string profile, string targetId, string reference)
    {
        var key = SnapshotKey(profile, targetId);
        if (!_snapshots.TryGetValue(key, out var snapshot))
        {
            throw new InvalidOperationException("Snapshot not found. Run action=snapshot first.");
        }

        var normalized = reference.Trim().TrimStart('e');
        if (snapshot.Refs.TryGetValue(normalized, out var selector))
        {
            return selector;
        }

        throw new InvalidOperationException($"Unknown ref: {reference}. Run action=snapshot again.");
    }

    private void CleanupSnapshotsForProfile(string profile)
    {
        foreach (var key in _snapshots.Keys)
        {
            if (key.StartsWith(profile + "::", StringComparison.OrdinalIgnoreCase))
            {
                _snapshots.TryRemove(key, out _);
            }
        }
    }

    private sealed class SnapshotNode
    {
        [JsonPropertyName("ref")]
        public string? Ref { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("tag")]
        public string? Tag { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
