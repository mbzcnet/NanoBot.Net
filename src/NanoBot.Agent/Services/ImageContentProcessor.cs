using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent.Services;

/// <summary>
/// Handles image content processing, URL conversion, and snapshot extraction.
/// </summary>
public sealed class ImageContentProcessor
{
    private static readonly Regex MarkdownImageRegex = new(
        @"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)(?:\s+""[^""]*"")?\)",
        RegexOptions.Compiled);

    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<ImageContentProcessor>? _logger;

    /// <summary>
    /// Context for extracted snapshot images.
    /// </summary>
    public record MarkdownImageContext(string Url, string AltText, int Index);

    public ImageContentProcessor(IWorkspaceManager workspace, ILogger<ImageContentProcessor>? logger = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger;
    }

    /// <summary>
    /// Converts a local session file path to a URL for WebUI access.
    /// </summary>
    public string? ToSessionFileUrl(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var normalized = imagePath.Replace('\\', '/');
        if (normalized.StartsWith("/api/files/sessions/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (Path.IsPathRooted(imagePath))
        {
            var sessionsRoot = _workspace.GetSessionsPath().Replace('\\', '/');
            if (!normalized.StartsWith(sessionsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return $"/api/files/local?path={Uri.EscapeDataString(imagePath)}";
            }

            normalized = normalized[sessionsRoot.Length..].TrimStart('/');
        }

        if (normalized.StartsWith("sessions/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["sessions/".Length..];
        }

        normalized = normalized.TrimStart('/');
        return string.IsNullOrWhiteSpace(normalized) ? null : $"/api/files/sessions/{normalized}";
    }

    /// <summary>
    /// Converts a session file URL back to a local filesystem path.
    /// </summary>
    public string? ToLocalSessionFilePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        if (imagePath.StartsWith("/api/files/sessions/", StringComparison.OrdinalIgnoreCase))
        {
            var relative = imagePath["/api/files/sessions/".Length..].TrimStart('/');
            return Path.Combine(_workspace.GetSessionsPath(), relative.Replace('/', Path.DirectorySeparatorChar));
        }

        if (Path.IsPathRooted(imagePath))
        {
            if (File.Exists(imagePath))
            {
                return imagePath;
            }

            var sessionsRoot = _workspace.GetSessionsPath();
            if (imagePath.StartsWith(sessionsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return imagePath;
            }

            return null;
        }

        var normalized = imagePath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("sessions/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["sessions/".Length..];
        }

        var sessionsRoot2 = _workspace.GetSessionsPath();
        var sessionPath = Path.Combine(sessionsRoot2, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(sessionPath))
        {
            return sessionPath;
        }

        var workspaceRoot = _workspace.GetWorkspacePath();
        var workspacePath = Path.Combine(workspaceRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(workspacePath))
        {
            return workspacePath;
        }

        return null;
    }

    /// <summary>
    /// Builds a user message with text and optional images.
    /// </summary>
    public ChatMessage BuildUserMessage(string content, IEnumerable<string>? extraImageUrls = null)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrWhiteSpace(content))
        {
            contents.Add(new TextContent(content));
        }

        var imageUrls = ExtractMarkdownImageUrls(content);
        if (extraImageUrls != null)
        {
            imageUrls.AddRange(extraImageUrls.Where(static u => !string.IsNullOrWhiteSpace(u)));
        }

        foreach (var imageUrl in imageUrls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryLoadImageContent(imageUrl, out var imageContent))
            {
                continue;
            }

            contents.Add(imageContent);
        }

        return contents.Count switch
        {
            0 => new ChatMessage(ChatRole.User, string.Empty),
            1 when contents[0] is TextContent text => new ChatMessage(ChatRole.User, text.Text),
            _ => new ChatMessage(ChatRole.User, contents)
        };
    }

    /// <summary>
    /// Extracts image URLs from Markdown content.
    /// </summary>
    public List<string> ExtractMarkdownImageUrls(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var urls = new List<string>();
        var matches = MarkdownImageRegex.Matches(content);
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var url = match.Groups["url"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(url))
            {
                urls.Add(url);
            }
        }

        return urls;
    }

    /// <summary>
    /// Tries to load image content from a URL/path.
    /// </summary>
    public bool TryLoadImageContent(string imageUrl, out DataContent imageContent)
    {
        imageContent = default!;
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return false;
        }

        var localPath = ToLocalSessionFilePath(imageUrl);
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(localPath);
            if (bytes.Length == 0)
            {
                return false;
            }

            var mediaType = GetImageMediaType(localPath);
            imageContent = new DataContent(bytes, mediaType);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load image content for LLM request: {ImageUrl}", imageUrl);
            return false;
        }
    }

    /// <summary>
    /// Gets the MIME media type for an image file based on its extension.
    /// </summary>
    public string GetImageMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "image/png"
        };
    }

    /// <summary>
    /// Extracts snapshot image contexts from AI content, without injecting into Markdown text.
    /// </summary>
    public MarkdownImageContext[]? ExtractSnapshotImageContext(IEnumerable<AIContent> contents)
    {
        var images = new List<(string Url, int Index)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var content in contents)
        {
            if (content is not FunctionResultContent functionResult)
            {
                continue;
            }

            var payload = ToolHintFormatter.GetFunctionResultPayload(functionResult);
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(payload);
                var rootElement = document.RootElement;

                if (rootElement.ValueKind == JsonValueKind.String)
                {
                    var innerJson = rootElement.GetString();
                    if (!string.IsNullOrWhiteSpace(innerJson))
                    {
                        try
                        {
                            using var innerDoc = JsonDocument.Parse(innerJson);
                            ExtractSnapshotUrls(innerDoc.RootElement, seen, images, ref index);
                        }
                        catch (JsonException) { }
                    }
                }
                else
                {
                    ExtractSnapshotUrls(rootElement, seen, images, ref index);
                }
            }
            catch (JsonException)
            {
            }
        }

        if (images.Count == 0)
        {
            return null;
        }

        return images.Select((img, i) => new MarkdownImageContext(img.Url, $"snapshot-{i + 1}", i)).ToArray();
    }

    private void ExtractSnapshotUrls(JsonElement rootElement, HashSet<string> seen, List<(string Url, int Index)> images, ref int index)
    {
        if (rootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!TryGetJsonString(rootElement, "action", out var action) ||
            string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        if (!string.Equals(action, "snapshot", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action, "capture", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!TryGetJsonString(rootElement, "imagePath", out var imagePath) ||
            string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        var imageUrl = ToSessionFileUrl(imagePath);
        if (string.IsNullOrWhiteSpace(imageUrl) || !seen.Add(imageUrl!))
        {
            return;
        }

        images.Add((imageUrl, index++));
    }

    private static bool TryGetJsonString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.GetRawText();
            return true;
        }

        return false;
    }
}
