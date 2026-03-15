using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace NanoBot.Agent;

public class ResponseEvaluator : IResponseEvaluator
{
    private readonly ILogger<ResponseEvaluator>? _logger;

    private const string SystemPrompt = """
        You are a notification gate for a background agent.
        You will be given the original task and the agent's response.
        Call the evaluate_notification tool to decide whether the user
        should be notified.

        Notify when the response contains actionable information, errors,
        completed deliverables, or anything the user explicitly asked to
        be reminded about.

        Suppress when the response is a routine status check with nothing
        new, a confirmation that everything is normal, or essentially empty.
        """;

    public ResponseEvaluator(ILogger<ResponseEvaluator>? logger = null)
    {
        _logger = logger;
    }

    public async Task<bool> ShouldNotifyAsync(
        string response,
        string taskContext,
        IChatClient chatClient,
        CancellationToken ct = default)
    {
        try
        {
            var evaluateTool = AIFunctionFactory.Create(
                (string shouldNotify, string? reason) => (shouldNotify, reason ?? ""),
                new AIFunctionFactoryOptions
                {
                    Name = "evaluate_notification",
                    Description = "Decide whether the user should be notified about this background task result."
                });

            var responseMessage = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, SystemPrompt),
                    new ChatMessage(ChatRole.User, $"## Original task\n{taskContext}\n\n## Agent response\n{response}")
                ],
                new ChatOptions
                {
                    Tools = [evaluateTool],
                    MaxOutputTokens = 256,
                    Temperature = 0f
                },
                ct);

            var toolCall = responseMessage.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .FirstOrDefault(fc => string.Equals(fc.Name, "evaluate_notification", StringComparison.OrdinalIgnoreCase));

            if (toolCall?.Arguments is not IDictionary<string, object?> args)
            {
                _logger?.LogWarning("evaluate_response: no tool call returned, defaulting to notify");
                return true;
            }

            var shouldNotify = args.TryGetValue("shouldNotify", out var notifyValue) 
                && bool.TryParse(notifyValue?.ToString(), out var notifyResult) 
                && notifyResult;

            var reason = args.TryGetValue("reason", out var reasonValue) 
                ? reasonValue?.ToString() ?? "" 
                : "";

            _logger?.LogInformation("evaluate_response: should_notify={}, reason={}", shouldNotify, reason);
            return shouldNotify;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "evaluate_response failed, defaulting to notify");
            return true;
        }
    }
}
