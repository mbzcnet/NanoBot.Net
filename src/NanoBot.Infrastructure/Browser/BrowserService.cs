using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using NanoBot.Core.Tools.Browser;
using NanoBot.Core.Workspace;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace NanoBot.Infrastructure.Browser;

public sealed class BrowserService : IBrowserService
{
    private const int DefaultContentMaxChars = 8000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, ProfileState> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private IPlaywright? _playwright;
    private readonly ConcurrentDictionary<string, BrowserRefSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<BrowserService>? _logger;

    public BrowserService(IWorkspaceManager workspace, ILogger<BrowserService>? logger = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger;
    }

    public async Task<bool> IsStartedAsync(string profile, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return _profiles.ContainsKey(NormalizeProfile(profile));
    }

    public async Task EnsureStartedAsync(string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        if (_profiles.ContainsKey(normalized))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_profiles.ContainsKey(normalized))
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
                Profile = normalized,
                Browser = browser,
                Context = context,
                Pages = new ConcurrentDictionary<string, IPage>(StringComparer.Ordinal),
                NextTargetId = 0
            };

            _profiles[normalized] = state;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrowserToolResponse> GetStatusAsync(string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        var started = await IsStartedAsync(normalized, cancellationToken);
        var tabs = started ? await GetTabsAsync(normalized, cancellationToken) : [];

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
        await EnsureStartedAsync(normalized, cancellationToken);

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "start",
            Profile = normalized,
            Message = "Browser started"
        };
    }

    public async Task<BrowserToolResponse> StopAsync(string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        if (!_profiles.TryRemove(normalized, out var state))
        {
            return new BrowserToolResponse
            {
                Ok = true,
                Action = "stop",
                Profile = normalized,
                Message = "Browser not started"
            };
        }

        await state.Context.CloseAsync();
        await state.Browser.CloseAsync();
        CleanupSnapshotsForProfile(normalized);

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "stop",
            Profile = normalized,
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

    public async Task<BrowserToolResponse> OpenTabAsync(string url, string profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("targetUrl is required", nameof(url));
        }

        var normalized = NormalizeProfile(profile);
        var (targetId, page) = await CreatePageAsync(normalized, url, cancellationToken);

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
        var page = await GetPageAsync(normalized, targetId, cancellationToken);

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
        var page = await GetPageAsync(normalized, targetId, cancellationToken);
        await page.CloseAsync();
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
        var page = await GetPageAsync(normalized, targetId, cancellationToken);

        var useAriaSnapshot = string.Equals(format, "aria", StringComparison.OrdinalIgnoreCase);
        var ariaSnapshot = useAriaSnapshot ? await page.Locator(":root").AriaSnapshotAsync() : null;
        
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = new List<string>();

        if (useAriaSnapshot && !string.IsNullOrWhiteSpace(ariaSnapshot))
        {
            var snapshotLines = ariaSnapshot.Split('\n');
            var refCounter = 0;
            var roleTracker = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in snapshotLines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    continue;
                }

                var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"^-\s*(\w+)(?:\s+""([^""]*)"")?");
                if (!match.Success)
                {
                    lines.Add(line);
                    continue;
                }

                var role = match.Groups[1].Value.ToLowerInvariant();
                var name = match.Groups[2].Success ? match.Groups[2].Value : null;

                var isInteractive = IsInteractiveRole(role);
                if (isInteractive)
                {
                    refCounter++;
                    var refId = $"e{refCounter}";
                    
                    var roleKey = string.IsNullOrEmpty(name) ? role : $"{role}:{name}";
                    if (!roleTracker.ContainsKey(roleKey))
                    {
                        roleTracker[roleKey] = 0;
                    }
                    roleTracker[roleKey]++;

                    refs[refId] = $"[data-nbot-ref=\"{refId}\"]";
                    
                    var lineWithRef = $"{line} [ref={refId}]";
                    lines.Add(lineWithRef);
                }
                else
                {
                    lines.Add(line);
                }
            }
        }
        else
        {
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
            Message = useAriaSnapshot
                ? "ARIA snapshot captured using Playwright ARIA Snapshot API"
                : "Snapshot captured using DOM query"
        };
    }

    public async Task<BrowserToolResponse> CaptureSnapshotAsync(string targetId, string format, string profile, string? sessionKey, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        var page = await GetPageAsync(normalized, targetId, cancellationToken);
        // Use underscore instead of colon to avoid path issues on Windows and URL encoding issues
        var effectiveSessionKey = string.IsNullOrWhiteSpace(sessionKey)
            ? $"fallback_{normalized}"
            : sessionKey;

        string? imageRelativePath = null;

        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            _logger?.LogWarning("Snapshot called without sessionKey. Fallback key applied: {SessionKey}", effectiveSessionKey);
        }

        try
        {
            var screenshotsDir = GetSessionScreenshotsPath(effectiveSessionKey);
            Directory.CreateDirectory(screenshotsDir);

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmssfff");
            var screenshotFileName = $"snapshot_{timestamp}.png";
            var screenshotPath = Path.Combine(screenshotsDir, screenshotFileName);

            var screenshotBytes = await page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = true,
                Type = ScreenshotType.Png
            });

            var imagePath = await CompressAndSaveImageAsync(screenshotBytes, screenshotPath);
            imageRelativePath = Path.GetRelativePath(_workspace.GetSessionsPath(), imagePath);
            var imageUrl = $"/api/files/sessions/{imageRelativePath.Replace('\\', '/')}";
            _logger?.LogInformation("Snapshot image saved. sessionKey={SessionKey}, filePath={FilePath}, relativePath={RelativePath}, url={Url}", effectiveSessionKey, imagePath, imageRelativePath, imageUrl);
        }
        catch (Exception ex)
        {
            imageRelativePath = null;
            _logger?.LogWarning(ex, "Snapshot image save failed. sessionKey={SessionKey}, targetId={TargetId}, profile={Profile}", effectiveSessionKey, targetId, normalized);
        }

        var useAriaSnapshot = string.Equals(format, "aria", StringComparison.OrdinalIgnoreCase);
        var ariaSnapshot = useAriaSnapshot ? await page.Locator(":root").AriaSnapshotAsync() : null;
        
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = new List<string>();

        if (useAriaSnapshot && !string.IsNullOrWhiteSpace(ariaSnapshot))
        {
            var snapshotLines = ariaSnapshot.Split('\n');
            var refCounter = 0;
            var roleTracker = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in snapshotLines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    continue;
                }

                var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"^-\s*(\w+)(?:\s+""([^""]*)"")?");
                if (!match.Success)
                {
                    lines.Add(line);
                    continue;
                }

                var role = match.Groups[1].Value.ToLowerInvariant();
                var name = match.Groups[2].Success ? match.Groups[2].Value : null;

                var isInteractive = IsInteractiveRole(role);
                if (isInteractive)
                {
                    refCounter++;
                    var refId = $"e{refCounter}";
                    
                    var roleKey = string.IsNullOrEmpty(name) ? role : $"{role}:{name}";
                    if (!roleTracker.ContainsKey(roleKey))
                    {
                        roleTracker[roleKey] = 0;
                    }
                    roleTracker[roleKey]++;

                    refs[refId] = $"[data-nbot-ref=\"{refId}\"]";
                    
                    var lineWithRef = $"{line} [ref={refId}]";
                    lines.Add(lineWithRef);
                }
                else
                {
                    lines.Add(line);
                }
            }
        }
        else
        {
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

        var message = string.IsNullOrWhiteSpace(imageRelativePath)
            ? (useAriaSnapshot ? "ARIA snapshot captured using Playwright ARIA Snapshot API" : "Snapshot captured using DOM query")
            : $"Snapshot captured with screenshot: {Path.GetFileName(imageRelativePath)}";

        return new BrowserToolResponse
        {
            Ok = true,
            Action = "snapshot",
            Profile = normalized,
            TargetId = targetId,
            Url = page.Url,
            Snapshot = snapshotText,
            Refs = refs,
            ImagePath = imageRelativePath,
            Message = message
        };
    }

    private string GetSessionScreenshotsPath(string sessionKey)
    {
        var normalizedSessionFolder = sessionKey;
        if (sessionKey.StartsWith("webui:", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSessionFolder = sessionKey["webui:".Length..];
        }

        var safeKey = normalizedSessionFolder.Replace(":", "_").Replace("/", "_").Replace("\\", "_");
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safeKey = safeKey.Replace(c, '_');
        }
        return Path.Combine(_workspace.GetSessionsPath(), safeKey, "screenshots");
    }

    private async Task<string> CompressAndSaveImageAsync(byte[] originalBytes, string outputPath)
    {
        try
        {
            await using var inputStream = new MemoryStream(originalBytes);
            using var image = await Image.LoadAsync(inputStream);
            var newWidth = Math.Max(1, (int)Math.Round(image.Width * 0.5));
            var newHeight = Math.Max(1, (int)Math.Round(image.Height * 0.5));

            image.Mutate(ctx => ctx.Resize(newWidth, newHeight));
            await image.SaveAsPngAsync(outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            await File.WriteAllBytesAsync(outputPath, originalBytes);
            _logger?.LogWarning(ex, "Image resize with ImageSharp failed, fallback to original bytes. outputPath={OutputPath}", outputPath);
            return outputPath;
        }
    }

    public async Task<BrowserToolResponse> GetContentAsync(string targetId, string? selector, int? maxChars, string profile, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        var page = await GetPageAsync(normalized, targetId, cancellationToken);
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
        var page = await GetPageAsync(normalized, targetId, cancellationToken);

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

    public async Task<IPage> GetPageAsync(string profile, string targetId, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProfile(profile);
        var state = await GetOrStartProfileAsync(normalized, cancellationToken);
        RefreshClosedPages(state);

        if (state.Pages.TryGetValue(targetId, out var page))
        {
            return page;
        }

        throw new InvalidOperationException($"Tab not found: {targetId}");
    }

    public void Dispose()
    {
        StopAllAsync().GetAwaiter().GetResult();
        _gate.Dispose();
    }

    private async Task<ProfileState> GetOrStartProfileAsync(string profile, CancellationToken cancellationToken)
    {
        var normalized = NormalizeProfile(profile);
        await EnsureStartedAsync(normalized, cancellationToken);

        if (_profiles.TryGetValue(normalized, out var state))
        {
            return state;
        }

        throw new InvalidOperationException($"Browser profile unavailable: {normalized}");
    }

    private async Task<(string TargetId, IPage Page)> CreatePageAsync(string profile, string? url, CancellationToken cancellationToken)
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

    private static string NormalizeProfile(string profile)
    {
        return string.IsNullOrWhiteSpace(profile) ? "nanobot" : profile.Trim();
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

        var normalized = reference.Trim();
        
        if (snapshot.Refs.TryGetValue(normalized, out var selector))
        {
            return selector;
        }

        var trimmedRef = normalized.TrimStart('e');
        if (snapshot.Refs.TryGetValue(trimmedRef, out var trimmedSelector))
        {
            return trimmedSelector;
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

    private static bool IsInteractiveRole(string role)
    {
        var interactiveRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "button",
            "link",
            "textbox",
            "checkbox",
            "radio",
            "combobox",
            "listbox",
            "menuitem",
            "menuitemcheckbox",
            "menuitemradio",
            "option",
            "searchbox",
            "slider",
            "spinbutton",
            "switch",
            "tab",
            "treeitem"
        };
        return interactiveRoles.Contains(role);
    }

    private sealed class ProfileState
    {
        public required string Profile { get; init; }
        public required IBrowser Browser { get; init; }
        public required IBrowserContext Context { get; init; }
        public required ConcurrentDictionary<string, IPage> Pages { get; init; }
        public int NextTargetId;
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
