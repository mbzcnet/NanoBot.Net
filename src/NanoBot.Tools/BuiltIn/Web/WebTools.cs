using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using NanoBot.Core.Configuration;

namespace NanoBot.Tools.BuiltIn;

public static class WebTools
{
    /// <summary>
    /// Creates a unified web page tool that supports both search and fetch modes.
    /// </summary>
    public static AITool CreateWebPageTool(HttpClient? httpClient = null, WebToolsConfig? config = null)
    {
        return AIFunctionFactory.Create(
            (string url, string? mode = "fetch", string? query = null, int maxResults = 5) =>
                WebPageAsync(url, mode, query, maxResults, httpClient, config),
            new AIFunctionFactoryOptions
            {
                Name = "web_page",
                Description = """
                    Retrieve web information using search or fetch mode.

                    Mode "search": Search DuckDuckGo for information and return results.
                    Mode "fetch": Fetch and extract text content from a URL.

                    Parameters:
                    - url: Target URL (required)
                    - mode: "search" or "fetch" (default: "fetch")
                    - query: Search query (required when mode="search")
                    - maxResults: Maximum search results (default: 5, only for search mode)

                    Examples:
                    - web_page(url="https://example.com") -> fetches page content
                    - web_page(url="", mode="search", query="latest news", maxResults=3) -> searches the web
                    """
            });
    }

    private static async Task<string> WebPageAsync(
        string url,
        string? mode,
        string? query,
        int maxResults,
        HttpClient? httpClient,
        WebToolsConfig? config)
    {
        var resolvedMode = mode?.Trim().ToLowerInvariant() ?? "fetch";

        return resolvedMode switch
        {
            "search" => await WebSearchAsync(query ?? url, maxResults, httpClient, config),
            "fetch" => await WebFetchAsync(url, httpClient, config),
            _ => $"Error: Unknown mode '{mode}'. Use 'search' or 'fetch'."
        };
    }

    private static async Task<string> WebSearchAsync(string query, int maxResults, HttpClient? httpClient, WebToolsConfig? config)
    {
        try
        {
            var ownClient = httpClient is null;
            var client = httpClient ?? new HttpClient();

            try
            {
                var searchUrl = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1";
                var response = await client.GetStringAsync(searchUrl);

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var results = new List<string>();

                if (root.TryGetProperty("RelatedTopics", out var topics))
                {
                    foreach (var topic in topics.EnumerateArray().Take(maxResults > 0 ? maxResults : 5))
                    {
                        if (topic.TryGetProperty("Text", out var text) &&
                            topic.TryGetProperty("FirstURL", out var topicUrl))
                        {
                            results.Add($"- {text.GetString()}\n  URL: {topicUrl.GetString()}");
                        }
                    }
                }

                if (results.Count == 0)
                {
                    return "No search results found.";
                }

                return string.Join("\n\n", results);
            }
            finally
            {
                if (ownClient)
                {
                    client.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            return $"Error searching web: {ex.Message}";
        }
    }

    private static async Task<string> WebFetchAsync(string url, HttpClient? httpClient, WebToolsConfig? config)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Error: URL is required for fetch mode.";
        }

        try
        {
            var ownClient = httpClient is null;
            var client = httpClient ?? new HttpClient();

            try
            {
                var userAgent = config?.FetchUserAgent ?? "Mozilla/5.0 (compatible; NanoBot/1.0)";
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

                var html = await client.GetStringAsync(url);

                var text = ExtractTextFromHtml(html);

                if (text.Length > 10000)
                {
                    text = text[..10000] + "\n... (truncated)";
                }

                return text;
            }
            finally
            {
                if (ownClient)
                {
                    client.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            return $"Error fetching URL: {ex.Message}";
        }
    }

    private static string ExtractTextFromHtml(string html)
    {
        var text = Regex.Replace(html, "<script[^>]*>.*?</script>", "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<style[^>]*>.*?</style>", "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }
}
