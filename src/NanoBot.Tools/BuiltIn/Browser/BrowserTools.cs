using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using NanoBot.Core.Tools;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Tools.BuiltIn;

public static class BrowserTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void SetCurrentSessionKey(string? sessionKey)
    {
        ToolExecutionContext.SetCurrentSessionKey(sessionKey);
    }

    public static AITool CreateBrowserTool(IBrowserService? browserService)
    {
        return AIFunctionFactory.Create(
            (string action,
             string? profile = null,
             string? targetId = null,
             string? targetUrl = null,
             string? url = null,
             string? snapshotFormat = null,
             string? kind = null,
             string? reference = null,
             string? text = null,
             string? textGone = null,
             string? key = null,
             int? timeoutMs = null,
             int? scrollBy = null,
             string? selector = null,
             int? maxChars = null,
             string? loadState = null,
             string? sessionKey = null) =>
                ExecuteAsync(action, profile, targetId, targetUrl, url, snapshotFormat, kind, reference, text, textGone, key, timeoutMs, scrollBy, selector, maxChars, loadState, sessionKey, browserService),
            new AIFunctionFactoryOptions
            {
                Name = "browser",
                Description = GetToolDescription()
            });
    }

    private static async Task<string> ExecuteAsync(
        string action,
        string? profile,
        string? targetId,
        string? targetUrl,
        string? url,
        string? snapshotFormat,
        string? kind,
        string? reference,
        string? text,
        string? textGone,
        string? key,
        int? timeoutMs,
        int? scrollBy,
        string? selector,
        int? maxChars,
        string? loadState,
        string? sessionKey,
        IBrowserService? browserService)
    {
        if (browserService == null)
        {
            return "Error: Browser service not available";
        }

        var resolvedAction = action?.Trim().ToLowerInvariant();
        var resolvedProfile = string.IsNullOrWhiteSpace(profile) ? "nanobot" : profile.Trim();
        var resolvedUrl = !string.IsNullOrWhiteSpace(url) ? url.Trim() : targetUrl?.Trim();

        var resolvedSessionKey = string.IsNullOrWhiteSpace(sessionKey)
            ? ToolExecutionContext.CurrentSessionKey
            : sessionKey.Trim();

        try
        {
            return resolvedAction switch
            {
                "status" => JsonSerializer.Serialize(await browserService.GetStatusAsync(resolvedProfile), JsonOptions),
                "start" => JsonSerializer.Serialize(await browserService.StartAsync(resolvedProfile), JsonOptions),
                "stop" => JsonSerializer.Serialize(await browserService.StopAsync(resolvedProfile), JsonOptions),
                "tabs" => JsonSerializer.Serialize(new BrowserToolResponse
                {
                    Ok = true,
                    Action = "tabs",
                    Profile = resolvedProfile,
                    Tabs = await browserService.GetTabsAsync(resolvedProfile)
                }, JsonOptions),
                "open" => JsonSerializer.Serialize(await browserService.OpenTabAsync(
                    resolvedUrl ?? string.Empty,
                    resolvedProfile), JsonOptions),
                "navigate" => JsonSerializer.Serialize(await browserService.NavigateAsync(
                    Require(targetId, "targetId"),
                    resolvedUrl ?? string.Empty,
                    resolvedProfile), JsonOptions),
                "close" => JsonSerializer.Serialize(await browserService.CloseTabAsync(
                    Require(targetId, "targetId"),
                    resolvedProfile), JsonOptions),
                "snapshot" => JsonSerializer.Serialize(await browserService.CaptureSnapshotAsync(
                    Require(targetId, "targetId"),
                    string.IsNullOrWhiteSpace(snapshotFormat) ? "ai" : snapshotFormat,
                    resolvedProfile,
                    resolvedSessionKey), JsonOptions),
                "capture" => JsonSerializer.Serialize(await browserService.CaptureSnapshotAsync(
                    Require(targetId, "targetId"),
                    string.IsNullOrWhiteSpace(snapshotFormat) ? "ai" : snapshotFormat,
                    resolvedProfile,
                    resolvedSessionKey), JsonOptions),
                "content" => JsonSerializer.Serialize(await browserService.GetContentAsync(
                    Require(targetId, "targetId"),
                    selector,
                    maxChars,
                    resolvedProfile), JsonOptions),
                "act" => JsonSerializer.Serialize(await browserService.ExecuteActionAsync(
                    new BrowserActionRequest
                    {
                        Kind = Require(kind, "kind"),
                        Ref = reference,
                        Text = text,
                        TextGone = textGone,
                        Key = key,
                        TimeoutMs = timeoutMs,
                        ScrollBy = scrollBy,
                        LoadState = loadState
                    },
                    Require(targetId, "targetId"),
                    resolvedProfile), JsonOptions),
                "wait" => JsonSerializer.Serialize(await browserService.ExecuteActionAsync(
                    new BrowserActionRequest
                    {
                        Kind = "wait",
                        Text = text,
                        TextGone = textGone,
                        TimeoutMs = timeoutMs,
                        LoadState = loadState
                    },
                    Require(targetId, "targetId"),
                    resolvedProfile), JsonOptions),
                _ => $"Error: Unknown action: {action}"
            };
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static string GetToolDescription()
    {
        return """
            Control browser tabs for web automation and data extraction.

            Actions and required parameters:
            - status: Get browser status. No required parameters.
            - start: Start browser instance. No required parameters.
            - stop: Stop browser instance. No required parameters.
            - tabs: List all open tabs. No required parameters.
            - open: Open a new tab. Required: url or targetUrl (the URL to open).
            - navigate: Navigate existing tab to URL. Required: targetId (tab ID), url or targetUrl (destination URL).
            - close: Close a tab. Required: targetId (tab ID to close).
            - snapshot: Get interactive page snapshot. Required: targetId (tab ID). Optional: snapshotFormat ('ai' or 'raw').
            - capture: Capture screenshot. Required: targetId (tab ID). Optional: snapshotFormat ('ai' or 'raw').
            - content: Extract page content. Required: targetId (tab ID). Optional: selector (CSS selector), maxChars (max characters to return).
            - act: Execute action on page. Required: targetId (tab ID), kind (action type: click, hover, scroll, press, wait). Optional: reference (element reference), text (input text), key (key to press), timeoutMs, scrollBy, loadState.
            - wait: Wait for page condition or timeout. Required: targetId (tab ID). Optional: text (wait for text to appear), textGone (wait for text to disappear), timeoutMs (default 3000ms), loadState (load, domcontentloaded, networkidle).

            Recommended workflow:
            1. Use 'open' or 'navigate' to load a page (remember the targetId returned).
            2. Use 'act' with kind='wait' to wait for page load if needed.
            3. Use 'snapshot' to get page structure with element references.
            4. Use 'act' to interact with elements (click, hover, etc.).
            5. Use 'content' to extract text content.

            Important: Always include targetId for navigate, close, snapshot, capture, content, and act actions.
            """;
    }

    private static string Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required");
        }

        return value.Trim();
    }
}
