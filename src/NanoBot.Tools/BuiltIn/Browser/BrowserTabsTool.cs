using System.Text.Json;
using Microsoft.Extensions.AI;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Tools.BuiltIn;

public static partial class BrowserTools
{
    /// <summary>
    /// List all open browser tabs.
    /// </summary>
    public static AITool CreateBrowserTabsTool(IBrowserService? browserService, Func<string?>? sessionKeyProvider = null)
    {
        return AIFunctionFactory.Create(
            (string? profile = null) =>
                BrowserTabsAsync(browserService, profile),
            new AIFunctionFactoryOptions
            {
                Name = "browser_tabs",
                Description = """
                    List all open browser tabs.

                    Parameters:
                    - profile: Browser profile name (optional, default: "nanobot")

                    Returns: JSON array of open tabs with their IDs and URLs.

                    Example: browser_tabs()
                    """
            });
    }

    private static async Task<string> BrowserTabsAsync(
        IBrowserService? browserService,
        string? profile)
    {
        if (browserService == null)
        {
            return """{"error": "Browser service not available"}""";
        }

        var resolvedProfile = string.IsNullOrWhiteSpace(profile) ? "nanobot" : profile.Trim();

        try
        {
            var tabs = await browserService.GetTabsAsync(resolvedProfile);
            return JsonSerializer.Serialize(new BrowserToolResponse
            {
                Ok = true,
                Action = "tabs",
                Profile = resolvedProfile,
                Tabs = tabs
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }
}
