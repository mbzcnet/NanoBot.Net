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
        IMemoryStore? memoryStore = null,
        int memoryWindow = 50,
        int maxInstructionChars = 0)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(skillsLoader);

        var instructions = BuildInstructions(workspace, options);

        var providers = new List<ChatHistoryProvider>
        {
            new FileBackedChatHistoryProvider(
                workspace,
                options?.MaxHistoryEntries ?? 100,
                loggerFactory?.CreateLogger<FileBackedChatHistoryProvider>())
        };

        var compositeProvider = new CompositeChatHistoryProvider(providers);

        var aiContextProviders = new List<AIContextProvider>
        {
            new CompositeAIContextProvider(
                workspace,
                skillsLoader,
                memoryStore,
                loggerFactory,
                maxInstructionChars)
        };

        var agentOptions = new ChatClientAgentOptions
        {
            Name = options?.Name ?? "NanoBot",
            Description = options?.Description ?? "A personal AI assistant",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools?.ToList()
            },
            ChatHistoryProvider = compositeProvider,
            AIContextProviders = aiContextProviders
        };

        return new ChatClientAgent(chatClient, agentOptions, loggerFactory);
    }

    public static string BuildInstructions(IWorkspaceManager workspace, AgentOptions? options = null)
    {
        var sb = new StringBuilder();
        var workspacePath = workspace.GetWorkspacePath();

        sb.AppendLine(GetIdentitySection(workspacePath, options));

        // Note: AGENTS.md, SOUL.md, USER.md, TOOLS.md are loaded by BootstrapContextProvider
        // via the AIContextProvider pipeline. Do NOT load them here to avoid duplication
        // in the system prompt (the framework concatenates all provider instructions).

        return sb.ToString();
    }

    private static string GetIdentitySection(string workspacePath, AgentOptions? options)
    {
        var name = options?.Name ?? "NanoBot";
        var runtime = GetRuntimeInfo();

        return $@"# {name} 🐈

You are {name}, a helpful AI assistant. You have access to tools that allow you to:
- Read, write, and edit files
- Execute shell commands
- Search the web and fetch web pages
- Control a real browser tab (open, navigate, snapshot, act, content)
- Send messages to users on chat channels
- Spawn subagents for complex background tasks

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
    public int MaxIterations { get; set; } = 40;
    public float Temperature { get; set; } = 0.1f;
    public int MaxTokens { get; set; } = 4096;
}

internal class CompositeAIContextProvider : AIContextProvider
{
    private readonly BootstrapContextProvider _bootstrapProvider;
    private readonly MemoryContextProvider? _memoryProvider;
    private readonly SkillsContextProvider _skillsProvider;
    private readonly ILogger? _logger;
    private readonly int _maxInstructionChars;

    public CompositeAIContextProvider(
        IWorkspaceManager workspace,
        ISkillsLoader skillsLoader,
        IMemoryStore? memoryStore,
        ILoggerFactory? loggerFactory,
        int maxInstructionChars = 0)
    {
        _logger = loggerFactory?.CreateLogger<CompositeAIContextProvider>();
        _maxInstructionChars = maxInstructionChars;
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

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var inputMessages = context.AIContext.Messages?.ToList();
        var inputMessageCount = inputMessages?.Count ?? 0;
        _logger?.LogInformation("[DEBUG] CompositeAIContextProvider.ProvideAIContextAsync - Input messages: {Count}", inputMessageCount);
        if (inputMessages != null)
        {
            foreach (var msg in inputMessages)
            {
                var preview = msg.Text?.Length > 50 ? msg.Text[..50] + "..." : msg.Text;
                _logger?.LogInformation("[DEBUG]   - Message: role={Role}, text={Text}", msg.Role, preview);
            }
        }
        
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var instructions = new StringBuilder();

        var swBootstrap = System.Diagnostics.Stopwatch.StartNew();
        var bootstrapContext = await _bootstrapProvider.InvokingAsync(context, cancellationToken);
        swBootstrap.Stop();
        var bootstrapChars = bootstrapContext.Instructions?.Length ?? 0;
        if (!string.IsNullOrEmpty(bootstrapContext.Instructions))
        {
            instructions.AppendLine(bootstrapContext.Instructions);
        }

        var swMemory = System.Diagnostics.Stopwatch.StartNew();
        var memoryContext = _memoryProvider != null
            ? await _memoryProvider.InvokingAsync(context, cancellationToken)
            : new AIContext();
        swMemory.Stop();
        var memoryChars = memoryContext.Instructions?.Length ?? 0;
        if (!string.IsNullOrEmpty(memoryContext.Instructions))
        {
            instructions.AppendLine(memoryContext.Instructions);
        }

        var swSkills = System.Diagnostics.Stopwatch.StartNew();
        var skillsContext = await _skillsProvider.InvokingAsync(context, cancellationToken);
        swSkills.Stop();
        var skillsChars = skillsContext.Instructions?.Length ?? 0;
        if (!string.IsNullOrEmpty(skillsContext.Instructions))
        {
            instructions.AppendLine(skillsContext.Instructions);
        }

        // Append current time as dynamic context (after stable base instructions,
        // so Ollama can cache the KV state for the prefix)
        var now = DateTime.Now;
        var tz = TimeZoneInfo.Local;
        instructions.AppendLine($"\n## Current Time\n{now:yyyy-MM-dd HH:mm (dddd)} ({tz.DisplayName})");

        sw.Stop();
        _logger?.LogInformation(
            "[CONTEXT] Bootstrap: {BootstrapChars} chars ({BootstrapMs}ms), Memory: {MemoryChars} chars ({MemoryMs}ms), Skills: {SkillsChars} chars ({SkillsMs}ms), Total context: {TotalChars} chars ({TotalMs}ms)",
            bootstrapChars, swBootstrap.ElapsedMilliseconds,
            memoryChars, swMemory.ElapsedMilliseconds,
            skillsChars, swSkills.ElapsedMilliseconds,
            instructions.Length, sw.ElapsedMilliseconds);

        var result = instructions.ToString();
        if (_maxInstructionChars > 0 && result.Length > _maxInstructionChars)
        {
            _logger?.LogWarning(
                "[CONTEXT] Instructions truncated from {OriginalChars} to {MaxChars} chars. Set memory.maxInstructionChars in config to adjust.",
                result.Length, _maxInstructionChars);
            result = result[.._maxInstructionChars];
        }

        return new AIContext
        {
            Instructions = result.Length > 0 ? result : null,
            // Don't return Messages or Tools - the base class will merge them automatically
            Messages = null,
            Tools = null
        };
    }

    protected override async ValueTask StoreAIContextAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
        if (_memoryProvider != null)
        {
            await _memoryProvider.InvokedAsync(context, cancellationToken);
        }
    }
}
