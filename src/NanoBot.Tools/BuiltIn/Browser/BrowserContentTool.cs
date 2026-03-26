using System.Text.Json;
using Microsoft.Extensions.AI;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Tools.BuiltIn;

public static partial class BrowserTools
{
    /// <summary>
    /// Extract text content from browser page.
    /// </summary>
    public static AITool CreateBrowserContentTool(IBrowserService? browserService, Func<string?>? sessionKeyProvider = null)
    {
        return AIFunctionFactory.Create(
            (string targetId, string? selector = null, int? maxChars = null, string? profile = null) =>
                BrowserContentAsync(browserService, targetId, selector, maxChars, profile),
            new AIFunctionFactoryOptions
            {
                Name = "browser_content",
                Description = """
                    Extract text content from a browser page.

                    Parameters:
                    - targetId: Tab ID from browser_open result (required)
                    - selector: CSS selector to extract specific content (optional)
                    - maxChars: Maximum characters to return (optional, default: no limit)
                    - profile: Browser profile name (optional, default: "nanobot")

                    Returns: Extracted text content from the page.

                    Examples:
                    - browser_content(targetId="tab_123") -> extracts all page text
                    - browser_content(targetId="tab_123", selector="h1.title") -> extracts text from specific element
                    - browser_content(targetId="tab_123", maxChars=5000) -> limits output to 5000 chars
                    """
            });
    }

    private static async Task<string> BrowserContentAsync(
        IBrowserService? browserService,
        string targetId,
        string? selector,
        int? maxChars,
        string? profile)
    {
        if (browserService == null)
        {
            return "Error: Browser service not available";
        }

        var resolvedProfile = string.IsNullOrWhiteSpace(profile) ? "nanobot" : profile.Trim();
        var resolvedTargetId = targetId?.Trim() ?? throw new InvalidOperationException("targetId is required");

        try
        {
            var result = await browserService.GetContentAsync(resolvedTargetId, selector?.Trim(), maxChars, resolvedProfile);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }
}
