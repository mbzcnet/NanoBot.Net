using Microsoft.Extensions.AI;
using NanoBot.Core.Subagents;

namespace NanoBot.Tools.BuiltIn;

public static class SpawnTools
{
    public static AITool CreateSpawnTool(ISubagentManager? subagentManager, string? defaultChannel = null, string? defaultChatId = null)
    {
        return AIFunctionFactory.Create(
            (string task, string? label, CancellationToken cancellationToken) =>
                SpawnAsync(task, label, defaultChannel, defaultChatId, subagentManager, cancellationToken),
            new AIFunctionFactoryOptions
            {
                Name = "spawn",
                Description = "Spawn a subagent to handle a task in the background. Use this for complex or time-consuming tasks that can run independently."
            });
    }

    private static async Task<string> SpawnAsync(
        string task,
        string? label,
        string? defaultChannel,
        string? defaultChatId,
        ISubagentManager? subagentManager,
        CancellationToken cancellationToken)
    {
        try
        {
            if (subagentManager == null)
            {
                return "Error: Subagent manager not available";
            }

            if (string.IsNullOrEmpty(defaultChannel) || string.IsNullOrEmpty(defaultChatId))
            {
                return "Error: No session context (channel/chat_id)";
            }

            var result = await subagentManager.SpawnAsync(
                task,
                label,
                defaultChannel,
                defaultChatId,
                cancellationToken);

            return $"Subagent spawned with ID: {result.Id}";
        }
        catch (Exception ex)
        {
            return $"Error spawning subagent: {ex.Message}";
        }
    }
}
