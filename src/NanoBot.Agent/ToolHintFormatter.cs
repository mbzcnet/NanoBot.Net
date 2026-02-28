using Microsoft.Extensions.AI;

namespace NanoBot.Agent;

public static class ToolHintFormatter
{
    private const int MaxArgumentLength = 40;

    /// <summary>
    /// Format tool calls as concise hints with icon and newline
    /// </summary>
    /// <param name="toolCalls">The function call contents to format</param>
    /// <returns>Formatted tool hint string</returns>
    public static string FormatToolHint(IEnumerable<FunctionCallContent> toolCalls)
    {
        var hints = toolCalls.Select(FormatSingleToolCall);
        return $"\n🔧 {string.Join(", ", hints)}\n";
    }

    private static string FormatSingleToolCall(FunctionCallContent toolCall)
    {
        if (toolCall.Arguments == null || !toolCall.Arguments.Any())
        {
            return toolCall.Name;
        }

        var firstArgValue = toolCall.Arguments.Values.FirstOrDefault();
        
        if (firstArgValue is not string strValue)
        {
            return toolCall.Name;
        }

        if (string.IsNullOrEmpty(strValue))
        {
            return toolCall.Name;
        }

        if (strValue.Length > MaxArgumentLength)
        {
            return $"{toolCall.Name}(\"{strValue[..MaxArgumentLength]}…\")";
        }

        return $"{toolCall.Name}(\"{strValue}\")";
    }
}
