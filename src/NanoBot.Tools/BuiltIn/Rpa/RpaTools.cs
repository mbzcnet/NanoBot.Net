using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using NanoBot.Core.Tools.Rpa;

namespace NanoBot.Tools.BuiltIn.Rpa;

/// <summary>
/// RPA 工具提供器
/// </summary>
public static class RpaTools
{
    /// <summary>
    /// 创建 RPA 工具
    /// </summary>
    /// <param name="rpaService">RPA 服务</param>
    /// <returns>AITool 实例</returns>
    public static AITool CreateRpaTool(IRpaService rpaService)
    {
        return AIFunctionFactory.Create(
            (RpaFlowRequest request, CancellationToken cancellationToken) =>
                ExecuteRpaAsync(rpaService, request, cancellationToken),
            new AIFunctionFactoryOptions
            {
                Name = "rpa",
                Description = """
                    Execute RPA (Robotic Process Automation) operations on the desktop.

                    This tool supports:
                    - Mouse operations: move, click, double-click, right-click, drag
                    - Keyboard operations: type text, press keys, hotkeys
                    - Screen capture for vision-based analysis
                    - Wait operations for timing control

                    Use 'flows' to specify an array of operations to execute sequentially.
                    Each operation is executed in order, with optional delays between steps.

                    Example - Basic operations:
                    {
                      "flows": [
                        { "type": "move", "x": 100, "y": 200 },
                        { "type": "click" },
                        { "type": "type", "text": "Hello World" }
                      ]
                    }

                    Example - With Vision (OmniParser):
                    {
                      "flows": [
                        { "type": "screenshot", "outputRef": "desktop" },
                        { "type": "move", "x": "{{vision.desktop[0].bbox[0]}}", "y": "{{vision.desktop[0].bbox[1]}}" },
                        { "type": "click" }
                      ],
                      "enableVision": true
                    }

                    Example - Hotkey:
                    {
                      "flows": [
                        { "type": "hotkey", "keys": ["Ctrl", "C"] }
                      ]
                    }

                    Example - Drag and drop:
                    {
                      "flows": [
                        { "type": "drag", "fromX": 100, "fromY": 200, "toX": 300, "toY": 400 }
                      ]
                    }
                    """
            });
    }

    private static async Task<string> ExecuteRpaAsync(
        IRpaService rpaService,
        RpaFlowRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await rpaService.ExecuteFlowAsync(request, cancellationToken);

        var response = new RpaToolResponse
        {
            Success = result.Success,
            CompletedSteps = result.CompletedSteps,
            TotalSteps = request.Flows.Length,
            Error = result.Error,
            VisionSummary = result.VisionResults != null && result.VisionResults.Count > 0
                ? GenerateVisionSummary(result.VisionResults)
                : null
        };

        return JsonSerializer.Serialize(response, RpaToolResponseContext.Default.RpaToolResponse);
    }

    private static string GenerateVisionSummary(Dictionary<string, OmniParserResult> visionResults)
    {
        var summary = new List<string>();

        foreach (var (refName, result) in visionResults)
        {
            summary.Add($"[{refName}] Found {result.ParsedContent.Count} elements:");
            foreach (var element in result.ParsedContent.Take(10))
            {
                summary.Add($"  - {element.Type}: \"{element.Label}\" at [{element.Bbox[0]}, {element.Bbox[1]}, {element.Bbox[2]}, {element.Bbox[3]}] (conf: {element.Confidence:P0})");
            }
            if (result.ParsedContent.Count > 10)
            {
                summary.Add($"  ... and {result.ParsedContent.Count - 10} more elements");
            }
        }

        return string.Join("\n", summary);
    }
}

/// <summary>
/// RPA 工具响应
/// </summary>
public class RpaToolResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("completed_steps")]
    public int CompletedSteps { get; init; }

    [JsonPropertyName("total_steps")]
    public int TotalSteps { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("vision_summary")]
    public string? VisionSummary { get; init; }

    [JsonPropertyName("message")]
    public string Message => Success
        ? $"Completed {CompletedSteps}/{TotalSteps} steps successfully"
        : $"Failed after {CompletedSteps}/{TotalSteps} steps: {Error}";
}

/// <summary>
/// JSON 序列化上下文
/// </summary>
[JsonSerializable(typeof(RpaToolResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class RpaToolResponseContext : JsonSerializerContext
{
}
