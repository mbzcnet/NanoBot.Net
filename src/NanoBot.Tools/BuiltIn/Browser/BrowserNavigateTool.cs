using System.Text.Json;
using Microsoft.Extensions.AI;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Tools.BuiltIn;

public static partial class BrowserTools
{
    /// <summary>
    /// Navigate an existing browser tab to a new URL.
    /// </summary>
    public static AITool CreateBrowserNavigateTool(IBrowserService? browserService, Func<string?>? sessionKeyProvider = null)
    {
        return AIFunctionFactory.Create(
            (string targetId, string url, string? profile = null) =>
                BrowserNavigateAsync(browserService, targetId, url, profile),
            new AIFunctionFactoryOptions
            {
                Name = "browser_navigate",
                Description = """
                    Navigate an existing browser tab to a new URL.

                    Parameters:
                    - targetId: Tab ID from browser_open result (required)
                    - url: Destination URL (required)
                    - profile: Browser profile name (optional, default: "nanobot")

                    Returns: JSON with navigation result.

                    Example: browser_navigate(targetId="tab_123", url="https://example.com")
                    """
            });
    }

    private static async Task<string> BrowserNavigateAsync(
        IBrowserService? browserService,
        string targetId,
        string url,
        string? profile)
    {
        if (browserService == null)
        {
            return """{"error": "Browser service not available"}""";
        }

        var resolvedProfile = string.IsNullOrWhiteSpace(profile) ? "nanobot" : profile.Trim();
        var resolvedTargetId = targetId?.Trim() ?? throw new InvalidOperationException("targetId is required");
        var resolvedUrl = url?.Trim() ?? throw new InvalidOperationException("url is required");

        try
        {
            var result = await browserService.NavigateAsync(resolvedTargetId, resolvedUrl, resolvedProfile);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }
}
