using System.Text.Json;
using Microsoft.Extensions.AI;

namespace NanoBot.Agent;

/// <summary>
/// Formats tool calls for display in CLI and WebUI.
/// Aligned with OpenCode-style tool call formatting.
/// </summary>
public static class ToolHintFormatter
{
    private const int MaxArgumentLength = 50;
    private const int MaxArgumentsToShow = 2;

    /// <summary>
    /// Format tool calls as a parseable marker with icon and details
    /// Use Markdown format that can be parsed by the frontend
    /// </summary>
    /// <param name="toolCalls">The function call contents to format</param>
    /// <returns>Formatted tool hint string</returns>
    public static string FormatToolHint(IEnumerable<FunctionCallContent> toolCalls)
    {
        var hints = toolCalls.Select(FormatSingleToolCall);
        return $"[TOOL_CALL]{string.Join("|||", hints)}[/TOOL_CALL]";
    }

    /// <summary>
    /// Format a single tool call for display
    /// </summary>
    private static string FormatSingleToolCall(FunctionCallContent toolCall)
    {
        var toolName = toolCall.Name ?? "unknown";

        if (toolCall.Arguments == null || !toolCall.Arguments.Any())
        {
            return $"{toolName}()";
        }

        // Format arguments - show key=value pairs
        var argList = new List<string>();
        var argCount = 0;

        foreach (var arg in toolCall.Arguments)
        {
            if (argCount >= MaxArgumentsToShow)
            {
                argList.Add("…");
                break;
            }

            var argValue = FormatArgumentValue(arg.Value);
            if (!string.IsNullOrEmpty(argValue))
            {
                argList.Add($"{arg.Key}={argValue}");
                argCount++;
            }
        }

        var argsString = string.Join(", ", argList);
        return $"{toolName}({argsString})";
    }

    /// <summary>
    /// Format an argument value for display
    /// </summary>
    private static string FormatArgumentValue(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        string strValue;

        if (value is string str)
        {
            strValue = str;
        }
        else if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                strValue = jsonElement.GetString() ?? "";
            }
            else if (jsonElement.ValueKind == JsonValueKind.Number)
            {
                return jsonElement.GetRawText();
            }
            else if (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False)
            {
                return jsonElement.GetBoolean().ToString().ToLowerInvariant();
            }
            else
            {
                // For complex objects, show truncated JSON
                var json = jsonElement.GetRawText();
                strValue = json.Length > 30 ? json[..30] + "…" : json;
            }
        }
        else
        {
            strValue = value.ToString() ?? "";
        }

        // Truncate long strings and quote them
        if (strValue.Length > MaxArgumentLength)
        {
            strValue = strValue[..MaxArgumentLength] + "…";
        }

        // Quote strings that contain spaces or special characters
        if (strValue.Contains(' ') || strValue.Contains('\n') || strValue.Contains('\t'))
        {
            return $"\"{strValue.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t")}\"";
        }

        return strValue;
    }

    /// <summary>
    /// Get a human-readable description of what the tool is doing
    /// </summary>
    public static string GetToolDescription(string toolName, IDictionary<string, object?>? arguments)
    {
        if (arguments == null || arguments.Count == 0)
        {
            return $"Executing {toolName}…";
        }

        // Tool-specific descriptions
        return toolName.ToLowerInvariant() switch
        {
            "read_file" or "read" => arguments.TryGetValue("path", out var path)
                ? $"Reading file: {Truncate(path?.ToString(), 40)}"
                : "Reading file…",

            "write_file" or "write" => arguments.TryGetValue("path", out var writePath)
                ? $"Writing file: {Truncate(writePath?.ToString(), 40)}"
                : "Writing file…",

            "edit" or "edit_file" => arguments.TryGetValue("path", out var editPath)
                ? $"Editing file: {Truncate(editPath?.ToString(), 40)}"
                : "Editing file…",

            "list_dir" or "list" or "glob" => arguments.TryGetValue("path", out var listPath)
                ? $"Listing directory: {Truncate(listPath?.ToString(), 40)}"
                : "Listing directory…",

            "grep" or "search" => arguments.TryGetValue("pattern", out var pattern)
                ? $"Searching for: {Truncate(pattern?.ToString(), 30)}"
                : "Searching…",

            "web_search" => arguments.TryGetValue("query", out var query)
                ? $"Searching web: {Truncate(query?.ToString(), 40)}"
                : "Searching web…",

            "browser" or "browser_open" => arguments.TryGetValue("url", out var url)
                ? $"Opening browser: {Truncate(url?.ToString(), 50)}"
                : "Opening browser…",

            "exec" or "execute" or "shell" => arguments.TryGetValue("command", out var cmd)
                ? $"Executing: {Truncate(cmd?.ToString(), 40)}"
                : "Executing command…",

            "web_fetch" => arguments.TryGetValue("url", out var fetchUrl)
                ? $"Fetching: {Truncate(fetchUrl?.ToString(), 50)}"
                : "Fetching URL…",

            _ => $"Executing {toolName}…"
        };
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? "";
        }
        return value[..maxLength] + "…";
    }
}
