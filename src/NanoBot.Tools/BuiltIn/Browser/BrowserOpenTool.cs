using System.Text.Json;
using Microsoft.Extensions.AI;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Tools.BuiltIn;

public static partial class BrowserTools
{
    /// <summary>
    /// Open a new browser tab.
    /// </summary>
    public static AITool CreateBrowserOpenTool(IBrowserService? browserService, Func<string?>? sessionKeyProvider = null)
    {
        return AIFunctionFactory.Create(
            (string url, string? profile = null, string? sessionKey = null) =>
                BrowserOpenAsync(browserService, url, profile, sessionKey, sessionKeyProvider),
            new AIFunctionFactoryOptions
            {
                Name = "browser_open",
                Description = """
                    Open a new browser tab with the specified URL.

                    Parameters:
                    - url: The URL to open (required)
                    - profile: Browser profile name (optional, default: "nanobot")
                    - sessionKey: Session key for context (optional)

                    Returns: JSON with targetId for subsequent operations.

                    Example: browser_open(url="https://example.com")
                    """
            });
    }

    private static async Task<string> BrowserOpenAsync(
        IBrowserService? browserService,
        string url,
        string? profile,
        string? sessionKey,
        Func<string?>? sessionKeyProvider)
    {
        if (browserService == null)
        {
            return """{"error": "Browser service not available"}""";
        }

        var resolvedProfile = string.IsNullOrWhiteSpace(profile) ? "nanobot" : profile.Trim();
        var resolvedUrl = url?.Trim() ?? string.Empty;
        var resolvedSessionKey = ResolveSessionKey(sessionKey, sessionKeyProvider);

        try
        {
            var result = await browserService.OpenTabAsync(resolvedUrl, resolvedProfile);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }
}
