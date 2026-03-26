using System.Text.Json;
using Microsoft.Extensions.AI;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Tools.BuiltIn;

public static partial class BrowserTools
{
    /// <summary>
    /// Capture a screenshot of the browser page.
    /// </summary>
    public static AITool CreateBrowserScreenshotTool(IBrowserService? browserService, Func<string?>? sessionKeyProvider = null)
    {
        return AIFunctionFactory.Create(
            (string targetId, string? snapshotFormat = null, string? profile = null, string? sessionKey = null) =>
                BrowserScreenshotAsync(browserService, targetId, snapshotFormat, profile, sessionKey, sessionKeyProvider),
            new AIFunctionFactoryOptions
            {
                Name = "browser_screenshot",
                Description = """
                    Capture a screenshot of the current browser page.

                    Parameters:
                    - targetId: Tab ID from browser_open result (required)
                    - snapshotFormat: "ai" (default) for annotated screenshot, "raw" for plain image
                    - profile: Browser profile name (optional, default: "nanobot")
                    - sessionKey: Session key for context (optional)

                    Returns: Screenshot data in the specified format.

                    Example: browser_screenshot(targetId="tab_123", snapshotFormat="ai")
                    """
            });
    }

    private static async Task<string> BrowserScreenshotAsync(
        IBrowserService? browserService,
        string targetId,
        string? snapshotFormat,
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
        var resolvedFormat = string.IsNullOrWhiteSpace(snapshotFormat) ? "ai" : snapshotFormat.Trim();
        var resolvedSessionKey = ResolveSessionKey(sessionKey, sessionKeyProvider);

        try
        {
            var result = await browserService.CaptureSnapshotAsync(resolvedTargetId, resolvedFormat, resolvedProfile, resolvedSessionKey);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }
}
