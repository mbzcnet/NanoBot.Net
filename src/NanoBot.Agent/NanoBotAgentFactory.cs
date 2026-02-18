using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Agent.Context;
using NanoBot.Core.Memory;
using NanoBot.Core.Skills;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent;

public static class NanoBotAgentFactory
{
    public static ChatClientAgent Create(
        IChatClient chatClient,
        IWorkspaceManager workspace,
        ISkillsLoader skillsLoader,
        IReadOnlyList<AITool>? tools = null,
        ILoggerFactory? loggerFactory = null,
        AgentOptions? options = null,
        IMemoryStore? memoryStore = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(skillsLoader);

        var instructions = BuildInstructions(workspace, options);

        var agentOptions = new ChatClientAgentOptions
        {
            Name = options?.Name ?? "NanoBot",
            Description = options?.Description ?? "A personal AI assistant",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools?.ToList()
            },
            ChatHistoryProviderFactory = (context, ct) =>
            {
                var provider = new FileBackedChatHistoryProvider(
                    workspace,
                    options?.MaxHistoryEntries ?? 100,
                    loggerFactory?.CreateLogger<FileBackedChatHistoryProvider>());
                return new ValueTask<ChatHistoryProvider>(provider);
            },
            AIContextProviderFactory = (context, ct) =>
            {
                var provider = new CompositeAIContextProvider(
                    workspace,
                    skillsLoader,
                    memoryStore,
                    loggerFactory);
                return new ValueTask<AIContextProvider>(provider);
            }
        };

        return new ChatClientAgent(chatClient, agentOptions, loggerFactory);
    }

    public static string BuildInstructions(IWorkspaceManager workspace, AgentOptions? options = null)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;
        var tz = TimeZoneInfo.Local;
        var workspacePath = workspace.GetWorkspacePath();

        sb.AppendLine(GetIdentitySection(now, tz, workspacePath, options));

        var agentsPath = workspace.GetAgentsFile();
        if (File.Exists(agentsPath))
        {
            try
            {
                var content = File.ReadAllText(agentsPath);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine();
                    sb.AppendLine("## Agent Configuration");
                    sb.AppendLine(content);
                }
            }
            catch
            {
            }
        }

        var soulPath = workspace.GetSoulFile();
        if (File.Exists(soulPath))
        {
            try
            {
                var content = File.ReadAllText(soulPath);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine();
                    sb.AppendLine("## Personality");
                    sb.AppendLine(content);
                }
            }
            catch
            {
            }
        }

        return sb.ToString();
    }

    private static string GetIdentitySection(DateTime now, TimeZoneInfo tz, string workspacePath, AgentOptions? options)
    {
        var name = options?.Name ?? "NanoBot";
        var runtime = GetRuntimeInfo();

        return $@"# {name} 🐈

You are {name}, a helpful AI assistant. You have access to tools that allow you to:
- Read, write, and edit files
- Execute shell commands
- Search the web and fetch web pages
- Send messages to users on chat channels
- Spawn subagents for complex background tasks

## Current Time
{now:yyyy-MM-dd HH:mm (dddd)} ({tz.DisplayName})

## Runtime
{runtime}

## Workspace
Your workspace is at: {workspacePath}
- Long-term memory: {workspacePath}/memory/MEMORY.md
- History log: {workspacePath}/memory/HISTORY.md (grep-searchable)
- Custom skills: {workspacePath}/skills/{{skill-name}}/SKILL.md

IMPORTANT: When responding to direct questions or conversations, reply directly with your text response.
Only use the 'message' tool when you need to send a message to a specific chat channel (like WhatsApp).
For normal conversation, just respond with text - do not call the message tool.

Always be helpful, accurate, and concise. When using tools, think step by step: what you know, what you need, and why you chose this tool.
When remembering something important, write to {workspacePath}/memory/MEMORY.md
To recall past events, grep {workspacePath}/memory/HISTORY.md";
    }

    private static string GetRuntimeInfo()
    {
        var os = Environment.OSVersion;
        var platform = os.Platform switch
        {
            PlatformID.MacOSX => "macOS",
            PlatformID.Unix => "Linux",
            PlatformID.Win32NT => "Windows",
            _ => os.Platform.ToString()
        };

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString()
        };

        var runtimeVersion = Environment.Version.ToString();
        var isAot = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled ? "JIT" : "AOT";

        return $"{platform} {arch}, .NET {runtimeVersion} ({isAot})";
    }
}

public class AgentOptions
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int MaxHistoryEntries { get; set; } = 100;
    public int MaxIterations { get; set; } = 20;
    public float Temperature { get; set; } = 0.7f;
    public int MaxTokens { get; set; } = 4096;
}

internal class CompositeAIContextProvider : AIContextProvider
{
    private readonly BootstrapContextProvider _bootstrapProvider;
    private readonly MemoryContextProvider? _memoryProvider;
    private readonly SkillsContextProvider _skillsProvider;

    public CompositeAIContextProvider(
        IWorkspaceManager workspace,
        ISkillsLoader skillsLoader,
        IMemoryStore? memoryStore,
        ILoggerFactory? loggerFactory)
    {
        _bootstrapProvider = new BootstrapContextProvider(
            workspace,
            loggerFactory?.CreateLogger<BootstrapContextProvider>());
        _memoryProvider = new MemoryContextProvider(
            workspace,
            memoryStore,
            loggerFactory?.CreateLogger<MemoryContextProvider>());
        _skillsProvider = new SkillsContextProvider(
            skillsLoader,
            loggerFactory?.CreateLogger<SkillsContextProvider>());
    }

    public override JsonElement Serialize(JsonSerializerOptions? options = null)
    {
        return JsonDocument.Parse("{}").RootElement.Clone();
    }

    protected override async ValueTask<AIContext> InvokingCoreAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var instructions = new StringBuilder();

        var bootstrapContext = await _bootstrapProvider.InvokingAsync(context, cancellationToken);
        if (!string.IsNullOrEmpty(bootstrapContext.Instructions))
        {
            instructions.AppendLine(bootstrapContext.Instructions);
        }

        var memoryContext = await _memoryProvider.InvokingAsync(context, cancellationToken);
        if (!string.IsNullOrEmpty(memoryContext.Instructions))
        {
            instructions.AppendLine(memoryContext.Instructions);
        }

        var skillsContext = await _skillsProvider.InvokingAsync(context, cancellationToken);
        if (!string.IsNullOrEmpty(skillsContext.Instructions))
        {
            instructions.AppendLine(skillsContext.Instructions);
        }

        return new AIContext
        {
            Instructions = instructions.Length > 0 ? instructions.ToString() : null
        };
    }

    protected override async ValueTask InvokedCoreAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
        await _memoryProvider.InvokedAsync(context, cancellationToken);
    }
}
