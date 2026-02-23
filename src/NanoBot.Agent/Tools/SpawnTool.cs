using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent.Tools;

public static class SpawnTool
{
    public static AITool CreateSpawnTool(
        IChatClient chatClient,
        IWorkspaceManager workspace,
        ILoggerFactory? loggerFactory = null,
        int maxIterations = 15)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(workspace);

        [Description("Create a sub-agent to handle a specific task.")]
        async Task<string> SpawnAsync(
            [Description("The task for the sub-agent to handle")] string task,
            [Description("Optional label for the sub-agent")] string? label = null,
            CancellationToken cancellationToken = default)
        {
            var subAgentName = label ?? $"subagent_{Guid.NewGuid():N}";

            var subAgent = CreateSubAgent(chatClient, workspace, task, subAgentName, loggerFactory, maxIterations);

            try
            {
                var response = await subAgent.RunAsync(task, cancellationToken: cancellationToken);
                return $"Sub-agent {subAgentName} completed:\n{response.Text}";
            }
            catch (Exception ex)
            {
                return $"Sub-agent {subAgentName} failed: {ex.Message}";
            }
        }

        return AIFunctionFactory.Create(
            SpawnAsync,
            new AIFunctionFactoryOptions
            {
                Name = "spawn",
                Description = "Create a sub-agent to handle a specific task. Use this for complex or time-consuming tasks that can run independently."
            });
    }

    public static AIFunction CreateSubAgentAsFunction(
        IChatClient chatClient,
        IWorkspaceManager workspace,
        string task,
        string? name = null,
        ILoggerFactory? loggerFactory = null,
        int maxIterations = 15)
    {
        var subAgentName = name ?? $"subagent_{Guid.NewGuid():N}";
        var subAgent = CreateSubAgent(chatClient, workspace, task, subAgentName, loggerFactory, maxIterations);

        return subAgent.AsAIFunction(
            new AIFunctionFactoryOptions
            {
                Name = subAgentName,
                Description = $"Sub-agent for task: {task}"
            });
    }

    private static ChatClientAgent CreateSubAgent(
        IChatClient chatClient,
        IWorkspaceManager workspace,
        string task,
        string name,
        ILoggerFactory? loggerFactory,
        int maxIterations)
    {
        var instructions = BuildSubAgentInstructions(workspace, task);

        return new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = name,
                Description = $"Sub-agent for task: {task}",
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions,
                    Temperature = 0.1f,
                    MaxOutputTokens = 4096
                }
            },
            loggerFactory);
    }

    private static string BuildSubAgentInstructions(IWorkspaceManager workspace, string task)
    {
        var now = DateTime.Now;
        var tz = TimeZoneInfo.Local;
        var workspacePath = workspace.GetWorkspacePath();
        var skillsPath = workspace.GetSkillsPath();

        return $@"# Subagent

## Current Time
{now:yyyy-MM-dd HH:mm (dddd)} ({tz.DisplayName})

You are a subagent spawned by the main agent to complete a specific task.

## Assigned Task
{task}

## Rules
1. Stay focused - complete only the assigned task, nothing else
2. Your final response will be reported back to the main agent
3. Do not initiate conversations or take on side tasks
4. Be concise but informative in your findings

## What You Can Do
- Read and write files in the workspace
- Execute shell commands
- Search the web and fetch web pages
- Complete the task thoroughly

## What You Cannot Do
- Send messages directly to users (no message tool available)
- Spawn other subagents
- Access the main agent's conversation history

## Workspace
Your workspace is at: {workspacePath}
Skills are available at: {skillsPath} (read SKILL.md files as needed)

When you have completed the task, provide a clear summary of your findings or actions.";
    }
}
