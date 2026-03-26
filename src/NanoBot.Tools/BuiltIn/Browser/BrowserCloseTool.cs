using System.Text.Json;
using Microsoft.Extensions.AI;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Tools.BuiltIn;

public static partial class BrowserTools
{
    /// <summary>
    /// Close a browser tab.
    /// </summary>
    public static AITool CreateBrowserCloseTool(IBrowserService? browserService, Func<string?>? sessionKeyProvider = null)
    {
        return AIFunctionFactory.Create(
            (string targetId, string? profile = null) =>
                BrowserCloseAsync(browserService, targetId, profile),
            new AIFunctionFactoryOptions
            {
                Name = "browser_close",
                Description = """
                    Close a browser tab.

                    Parameters:
                    - targetId: Tab ID to close (required)
                    - profile: Browser profile name (optional, default: "nanobot")

                    Returns: JSON with close result.

                    Example: browser_close(targetId="tab_123")
                    """
            });
    }

    private static async Task<string> BrowserCloseAsync(
        IBrowserService? browserService,
        string targetId,
        string? profile)
    {
        if (browserService == null)
        {
            return """{"error": "Browser service not available"}""";
        }

        var resolvedProfile = string.IsNullOrWhiteSpace(profile) ? "nanobot" : profile.Trim();
        var resolvedTargetId = targetId?.Trim() ?? throw new InvalidOperationException("targetId is required");

        try
        {
            var result = await browserService.CloseTabAsync(resolvedTargetId, resolvedProfile);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }
}
