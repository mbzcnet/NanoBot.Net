using System.Text.Json;
using Microsoft.Extensions.AI;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Tools.BuiltIn;

public static partial class BrowserTools
{
    /// <summary>
    /// Execute interactions on browser elements (click, hover, scroll, type, press).
    /// </summary>
    public static AITool CreateBrowserInteractTool(IBrowserService? browserService, Func<string?>? sessionKeyProvider = null)
    {
        return AIFunctionFactory.Create(
            (string targetId,
             string kind,
             string? reference = null,
             string? text = null,
             string? textGone = null,
             string? key = null,
             int? timeoutMs = null,
             int? scrollBy = null,
             string? loadState = null,
             string? profile = null,
             string? sessionKey = null) =>
                BrowserInteractAsync(browserService, targetId, kind, reference, text, textGone, key, timeoutMs, scrollBy, loadState, profile, sessionKey, sessionKeyProvider),
            new AIFunctionFactoryOptions
            {
                Name = "browser_interact",
                Description = """
                    Execute interactions on browser page elements.

                    Parameters:
                    - targetId: Tab ID from browser_open result (required)
                    - kind: Action type - "click", "hover", "scroll", "press", "type" (required)
                    - reference: Element reference from browser_snapshot (optional, for click/hover/type)
                    - text: Text to type (optional, for type action)
                    - key: Key to press (optional, for press action, e.g., "Enter", "Escape")
                    - scrollBy: Pixels to scroll (optional, for scroll action, e.g., 100 or -100)
                    - textGone: Wait for text to disappear (optional)
                    - timeoutMs: Wait timeout in milliseconds (optional, default: 3000)
                    - loadState: Load state - "load", "domcontentloaded", "networkidle" (optional)
                    - profile: Browser profile name (optional, default: "nanobot")
                    - sessionKey: Session key for context (optional)

                    Examples:
                    - browser_interact(targetId="tab_123", kind="click", reference="btn_submit")
                    - browser_interact(targetId="tab_123", kind="type", reference="input_search", text="search query")
                    - browser_interact(targetId="tab_123", kind="scroll", scrollBy=200)
                    - browser_interact(targetId="tab_123", kind="press", key="Enter")
                    """
            });
    }

    private static async Task<string> BrowserInteractAsync(
        IBrowserService? browserService,
        string targetId,
        string kind,
        string? reference,
        string? text,
        string? textGone,
        string? key,
        int? timeoutMs,
        int? scrollBy,
        string? loadState,
        string? profile,
        string? sessionKey,
        Func<string?>? sessionKeyProvider)
    {
        if (browserService == null)
        {
            return """{"error": "Browser service not available"}""";
        }

        var resolvedProfile = string.IsNullOrWhiteSpace(profile) ? "nanobot" : profile.Trim();
        var resolvedTargetId = targetId?.Trim() ?? throw new InvalidOperationException("targetId is required");
        var resolvedKind = kind?.Trim().ToLowerInvariant() ?? throw new InvalidOperationException("kind is required");

        var request = new BrowserActionRequest
        {
            Kind = resolvedKind,
            Ref = reference?.Trim(),
            Text = text?.Trim(),
            TextGone = textGone?.Trim(),
            Key = key?.Trim(),
            TimeoutMs = timeoutMs,
            ScrollBy = scrollBy,
            LoadState = loadState?.Trim()
        };

        try
        {
            var result = await browserService.ExecuteActionAsync(request, resolvedTargetId, resolvedProfile);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }
}
