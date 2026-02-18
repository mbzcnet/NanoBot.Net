using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace NanoBot.Tools.BuiltIn;

public static class WebTools
{
    public static AITool CreateWebSearchTool(HttpClient? httpClient = null)
    {
        return AIFunctionFactory.Create(
            (string query, int maxResults) => WebSearchAsync(query, maxResults, httpClient),
            new AIFunctionFactoryOptions
            {
                Name = "web_search",
                Description = "Search the web for information. Returns a list of search results with titles, URLs, and snippets."
            });
    }

    public static AITool CreateWebFetchTool(HttpClient? httpClient = null)
    {
        return AIFunctionFactory.Create(
            (string url) => WebFetchAsync(url, httpClient),
            new AIFunctionFactoryOptions
            {
                Name = "web_fetch",
                Description = "Fetch the content of a web page and return it as text."
            });
    }

    private static async Task<string> WebSearchAsync(string query, int maxResults, HttpClient? httpClient)
    {
        try
        {
            using var client = httpClient ?? new HttpClient();
            
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
                        topic.TryGetProperty("FirstURL", out var url))
                    {
                        results.Add($"- {text.GetString()}\n  URL: {url.GetString()}");
                    }
                }
            }
            
            if (results.Count == 0)
            {
                return "No search results found.";
            }
            
            return string.Join("\n\n", results);
        }
        catch (Exception ex)
        {
            return $"Error searching web: {ex.Message}";
        }
    }

    private static async Task<string> WebFetchAsync(string url, HttpClient? httpClient)
    {
        try
        {
            using var client = httpClient ?? new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; NanoBot/1.0)");
            
            var html = await client.GetStringAsync(url);
            
            var text = ExtractTextFromHtml(html);
            
            if (text.Length > 10000)
            {
                text = text[..10000] + "\n... (truncated)";
            }
            
            return text;
        }
        catch (Exception ex)
        {
            return $"Error fetching URL: {ex.Message}";
        }
    }

    private static string ExtractTextFromHtml(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<script[^>]*>.*?</script>", "", 
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<style[^>]*>.*?</style>", "", 
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }
}
