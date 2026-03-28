using MudBlazor;
using NanoBot.WebUI.Components.Shared;

namespace NanoBot.WebUI.Services;

/// <summary>
/// Static formatting helpers for chat message rendering.
/// </summary>
public static class ChatFormattingService
{
    /// <summary>
    /// Returns the tool payload, or "{}" if empty/whitespace.
    /// </summary>
    public static string FormatToolPayload(string? payload)
        => string.IsNullOrWhiteSpace(payload) ? "{}" : payload;

    /// <summary>
    /// Returns the tool output, or "(no output)" if empty/whitespace.
    /// </summary>
    public static string FormatToolOutput(string? output)
        => string.IsNullOrWhiteSpace(output) ? "(no output)" : output;

    /// <summary>
    /// Determines whether a tool output should be rendered as Markdown
    /// (contains image syntax, file URLs, or Markdown table structure).
    /// </summary>
    public static bool ShouldRenderToolOutputAsMarkdown(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        return output.Contains("![", StringComparison.Ordinal) ||
               output.Contains("/api/files/sessions/", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("](", StringComparison.Ordinal) ||
               LooksLikeMarkdownTable(output);
    }

    /// <summary>
    /// Returns true if the output text contains a Markdown table structure
    /// (header line, separator line with |, -, : characters).
    /// </summary>
    public static bool LooksLikeMarkdownTable(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return false;

        for (var i = 0; i < lines.Length - 1; i++)
        {
            var header = lines[i];
            var separator = lines[i + 1];
            if (!header.Contains('|', StringComparison.Ordinal) ||
                !separator.Contains('|', StringComparison.Ordinal))
                continue;

            var normalizedSeparator = separator.Replace(" ", string.Empty);
            if (normalizedSeparator.All(ch => ch == '|' || ch == '-' || ch == ':'))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the output text looks like an error message.
    /// </summary>
    public static bool LooksLikeErrorOutput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("error", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("ERR_", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("SSL", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the plain-text content for clipboard copy of a chat message.
    /// </summary>
    public static string BuildCopyContent(ChatMessage message)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.Content))
            segments.Add(message.Content);

        foreach (var toolExecution in message.ToolExecutions)
        {
            segments.Add($"IN {toolExecution.Name}\n{FormatToolPayload(toolExecution.Arguments)}");
            segments.Add($"OUT\n{FormatToolOutput(toolExecution.Output)}");
        }

        return string.Join("\n\n", segments.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>
    /// Returns the MudBlazor icon name for a given message role.
    /// </summary>
    public static string GetMessageIcon(string role)
    {
        return role?.ToLowerInvariant() switch
        {
            "user" => Icons.Material.Filled.Person,
            "system" => Icons.Material.Filled.Info,
            _ => Icons.Material.Filled.SmartToy
        };
    }

    /// <summary>
    /// Returns the display name for the sender of a given message role.
    /// </summary>
    public static string GetMessageSender(string role)
    {
        return role?.ToLowerInvariant() switch
        {
            "user" => "您",
            "system" => "系统提示",
            _ => "AI 助手"
        };
    }
}
