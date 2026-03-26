using System.Text.Json;
using Microsoft.Extensions.AI;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Tools.BuiltIn;

public static partial class BrowserTools
{
    /// <summary>
    /// Get page structure with AI-friendly element detection.
    /// </summary>
    public static AITool CreateBrowserSnapshotTool(IBrowserService? browserService, Func<string?>? sessionKeyProvider = null)
    {
        return AIFunctionFactory.Create(
            (string targetId, string? snapshotFormat = "ai", string? profile = null, string? sessionKey = null) =>
                BrowserSnapshotAsync(browserService, targetId, snapshotFormat, profile, sessionKey, sessionKeyProvider),
            new AIFunctionFactoryOptions
            {
                Name = "browser_snapshot",
                Description = """
                    Get interactive page snapshot with AI-friendly element detection.

                    Parameters:
                    - targetId: Tab ID from browser_open result (required)
                    - snapshotFormat: "ai" (default) for AI-friendly format, "raw" for HTML dump
                    - profile: Browser profile name (optional, default: "nanobot")
                    - sessionKey: Session key for context (optional)

                    Returns: JSON with page elements, their references, and bounding boxes.

                    Example: browser_snapshot(targetId="tab_123", snapshotFormat="ai")
                    """
            });
    }

    private static async Task<string> BrowserSnapshotAsync(
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
