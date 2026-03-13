using System.Text.Json;
using Microsoft.Extensions.AI;

namespace NanoBot.Agent;

public static class ToolHintFormatter
{
    private const int MaxArgumentLength = 40;

    /// <summary>
    /// Format tool calls as a parseable marker with icon and details
    /// Use Markdown format that can be parsed by the frontend
    /// </summary>
    /// <param name="toolCalls">The function call contents to format</param>
    /// <returns>Formatted tool hint string</returns>
    public static string FormatToolHint(IEnumerable<FunctionCallContent> toolCalls)
    {
        var hints = toolCalls.Select(FormatSingleToolCall);
        return $"\n[TOOL_CALL]{string.Join("|||", hints)}[/TOOL_CALL]\n";
    }

    private static string FormatSingleToolCall(FunctionCallContent toolCall)
    {
        var toolName = toolCall.Name ?? "unknown";

        if (toolCall.Arguments == null || !toolCall.Arguments.Any())
        {
            return $"{toolName}()";
        }

        // 获取第一个参数的键和值
        var firstArg = toolCall.Arguments.First();
        var argName = firstArg.Key;
        var argValue = firstArg.Value;

        // 将参数值转换为字符串（仅处理字符串类型）
        string? argString = null;
        if (argValue is string strValue)
        {
            argString = strValue;
        }
        else if (argValue is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
        {
            argString = jsonElement.GetString();
        }

        if (string.IsNullOrEmpty(argString))
        {
            return $"{toolName}()";
        }

        // 返回带参数的工具调用（显示参数名和值）
        var displayArg = argString.Length > MaxArgumentLength
            ? argString[..MaxArgumentLength] + "…"
            : argString;

        return $"{toolName}(\"{displayArg}\")";
    }
}
