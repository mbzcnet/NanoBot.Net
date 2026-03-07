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
            (string action, string? profile = null, string? targetId = null, string? targetUrl = null, string? snapshotFormat = null, string? kind = null, string? reference = null, string? text = null, string? textGone = null, string? key = null, int? timeoutMs = null, int? scrollBy = null, string? selector = null, int? maxChars = null, string? loadState = null, string? sessionKey = null) =>
                ExecuteAsync(action, profile, targetId, targetUrl, snapshotFormat, kind, reference, text, textGone, key, timeoutMs, scrollBy, selector, maxChars, loadState, sessionKey, browserService),
            new AIFunctionFactoryOptions
            {
                Name = "browser",
                Description = "Control browser tabs with actions: status, start, stop, tabs, open, navigate, close, snapshot, capture, content, act. Recommended flow: open/navigate -> snapshot/capture -> act -> content. Use capture for screenshot with image output."
            });
    }

    private static async Task<string> ExecuteAsync(
        string action,
        string? profile,
        string? targetId,
        string? targetUrl,
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
        var resolvedProfile = string.IsNullOrWhiteSpace(profile) ? "openclaw" : profile.Trim();

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
                    targetUrl ?? string.Empty,
                    resolvedProfile), JsonOptions),
                "navigate" => JsonSerializer.Serialize(await browserService.NavigateAsync(
                    Require(targetId, "targetId"),
                    targetUrl ?? string.Empty,
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
                _ => $"Error: Unknown action: {action}"
            };
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
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
