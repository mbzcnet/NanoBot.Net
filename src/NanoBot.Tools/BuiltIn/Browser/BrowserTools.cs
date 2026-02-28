using System.Text.Json;
using Microsoft.Extensions.AI;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Tools.BuiltIn;

public static class BrowserTools
{
    public static AITool CreateBrowserTool(IBrowserService? browserService)
    {
        return AIFunctionFactory.Create(
            (string action, string? profile, string? targetId, string? targetUrl, string? snapshotFormat, string? kind, string? reference, string? text, string? textGone, string? key, int? timeoutMs, int? scrollBy, string? selector, int? maxChars, string? loadState) =>
                ExecuteAsync(action, profile, targetId, targetUrl, snapshotFormat, kind, reference, text, textGone, key, timeoutMs, scrollBy, selector, maxChars, loadState, browserService),
            new AIFunctionFactoryOptions
            {
                Name = "browser",
                Description = "Control browser tabs with actions: status, start, stop, tabs, open, navigate, close, snapshot, content, act. Recommended flow: open/navigate -> snapshot -> act -> content."
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
        IBrowserService? browserService)
    {
        if (browserService == null)
        {
            return "Error: Browser service not available";
        }

        var resolvedAction = action?.Trim().ToLowerInvariant();
        var resolvedProfile = string.IsNullOrWhiteSpace(profile) ? "openclaw" : profile.Trim();

        try
        {
            return resolvedAction switch
            {
                "status" => JsonSerializer.Serialize(await browserService.GetStatusAsync(resolvedProfile)),
                "start" => JsonSerializer.Serialize(await browserService.StartAsync(resolvedProfile)),
                "stop" => JsonSerializer.Serialize(await browserService.StopAsync(resolvedProfile)),
                "tabs" => JsonSerializer.Serialize(new BrowserToolResponse
                {
                    Ok = true,
                    Action = "tabs",
                    Profile = resolvedProfile,
                    Tabs = await browserService.GetTabsAsync(resolvedProfile)
                }),
                "open" => JsonSerializer.Serialize(await browserService.OpenTabAsync(
                    targetUrl ?? string.Empty,
                    resolvedProfile)),
                "navigate" => JsonSerializer.Serialize(await browserService.NavigateAsync(
                    Require(targetId, "targetId"),
                    targetUrl ?? string.Empty,
                    resolvedProfile)),
                "close" => JsonSerializer.Serialize(await browserService.CloseTabAsync(
                    Require(targetId, "targetId"),
                    resolvedProfile)),
                "snapshot" => JsonSerializer.Serialize(await browserService.GetSnapshotAsync(
                    Require(targetId, "targetId"),
                    string.IsNullOrWhiteSpace(snapshotFormat) ? "ai" : snapshotFormat,
                    resolvedProfile)),
                "content" => JsonSerializer.Serialize(await browserService.GetContentAsync(
                    Require(targetId, "targetId"),
                    selector,
                    maxChars,
                    resolvedProfile)),
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
                    resolvedProfile)),
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
