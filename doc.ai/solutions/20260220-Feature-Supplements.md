# NanoBot.Net 功能补充实现方案

> **文档版本**: 1.0  
> **创建日期**: 2026-02-20  
> **依据报告**: [2026-02-20-nanobot-updates.md](../reports/2026-02-20-nanobot-updates.md)  
> **目标**: 100% 完成移植版与原项目相比缺少或不完善的部分

---

## 概述

本文档基于 nanobot 原项目最近 4 天（2026-02-16 ~ 2026-02-20）的更新报告，详细定义移植版需要补充的功能实现方案。当前移植版整体完成率为 **26%**，本文档旨在指导开发团队完成剩余 **74%** 的功能实现。

### 实现状态汇总

| 类别 | 已实现 | 部分实现 | 未实现 | 完成率 | 优先级 |
|------|--------|---------|--------|--------|--------|
| Agent Loop | 1 | 2 | 3 | 17% | 🔴 高 |
| Shell 工具 | 0 | 1 | 3 | 0% | 🔴 高 |
| Provider | 1 | 1 | 6 | 14% | 🔴 高 |
| Channel | 1 | 4 | 3 | 20% | 🟡 中 |
| Cron 服务 | 0 | 2 | 1 | 0% | 🟡 中 |
| MCP | 1 | 0 | 1 | 50% | 🟢 低 |
| 编码国际化 | 2 | 0 | 0 | 100% | ✅ 完成 |
| Subagent | 1 | 0 | 0 | 100% | ✅ 完成 |

---

## 一、Agent Loop 核心功能补充

### 1.1 模型临时文本后工具调用重试

**问题背景**: MiniMax、Gemini Flash、GPT-4.1 等模型会先发送临时文本（如 "Let me investigate..."）再调用工具，当前移植版会立即终止循环导致工具无法执行。

**原项目实现**: `Temp/nanobot/nanobot/agent/loop.py:231-244`

**实现方案**:

```csharp
// src/NanoBot.Agent/AgentRuntime.cs

public sealed class AgentRuntime : IAgentRuntime, IDisposable
{
    private readonly HashSet<string> _consolidating = new();
    
    private async Task<AgentLoopResult> RunAgentLoopAsync(
        IList<ChatMessage> initialMessages,
        Func<string, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var messages = initialMessages.ToList();
        var iteration = 0;
        string? finalContent = null;
        var toolsUsed = new List<string>();
        var textOnlyRetried = false;

        while (iteration < _maxIterations)
        {
            iteration++;
            
            var response = await _agent.RunAsync(messages, cancellationToken: cancellationToken);
            var responseMessage = response.Messages.FirstOrDefault();
            
            if (response.ToolCalls != null && response.ToolCalls.Count > 0)
            {
                // 有工具调用
                if (onProgress != null)
                {
                    var cleanText = StripThinkTags(responseMessage?.Text);
                    if (!string.IsNullOrEmpty(cleanText))
                    {
                        await onProgress(cleanText);
                    }
                    await onProgress(FormatToolHint(response.ToolCalls));
                }
                
                // 处理工具调用...
                foreach (var toolCall in response.ToolCalls)
                {
                    toolsUsed.Add(toolCall.Name);
                    // 执行工具并追加结果
                }
            }
            else
            {
                finalContent = StripThinkTags(responseMessage?.Text);
                
                // 关键：支持模型临时文本后工具调用重试
                if (!toolsUsed.Any() && !textOnlyRetried && !string.IsNullOrEmpty(finalContent))
                {
                    textOnlyRetried = true;
                    _logger?.LogDebug("Interim text response (no tools used yet), retrying: {Preview}", 
                        finalContent[..Math.Min(80, finalContent.Length)]);
                    
                    // 追加 assistant 消息后继续循环
                    messages.Add(new ChatMessage(ChatRole.Assistant, responseMessage?.Text ?? string.Empty));
                    finalContent = null;
                    continue;
                }
                
                break;
            }
        }

        return new AgentLoopResult(finalContent, toolsUsed);
    }
    
    private static string? StripThinkTags(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return System.Text.RegularExpressions.Regex.Replace(text, @"<think[\s\S]*?</think >", "", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    }
    
    private static string FormatToolHint(IList<AIToolCall> toolCalls)
    {
        var hints = toolCalls.Select(tc =>
        {
            var firstArg = tc.Arguments?.Values.FirstOrDefault()?.ToString();
            if (firstArg == null) return tc.Name;
            var preview = firstArg.Length > 40 ? firstArg[..40] + "…" : firstArg;
            return $"{tc.Name}(\"{preview}\")";
        });
        return string.Join(", ", hints);
    }
}
```

**测试用例**:

```csharp
// tests/NanoBot.Agent.Tests/AgentRuntimeTests.cs

[Fact]
public async Task RunAgentLoop_ShouldRetryWhenModelSendsInterimText()
{
    // Arrange: 模拟模型先返回文本，再返回工具调用
    var mockAgent = new Mock<IChatClient>();
    mockAgent.SetupSequence(x => x.CompleteAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), default))
        .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Let me investigate...")))
        .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Result"), 
            toolCalls: new[] { new AIToolCall("search", new Dictionary<string, object?> { ["query"] = "test" }) }));
    
    // Act
    var result = await _runtime.RunAgentLoopAsync(messages);
    
    // Assert
    Assert.NotNull(result.Content);
    Assert.Contains("search", result.ToolsUsed);
}
```

---

### 1.2 内存整合重复任务防护

**问题背景**: 当消息超过 memory_window 阈值时，每条消息都可能触发新的整合任务，导致 API 调用风暴。

**原项目实现**: `Temp/nanobot/nanobot/agent/loop.py:92, 336-345`

**实现方案**:

```csharp
// src/NanoBot.Agent/AgentRuntime.cs

public sealed class AgentRuntime : IAgentRuntime, IDisposable
{
    private readonly HashSet<string> _consolidating = new(StringComparer.OrdinalIgnoreCase);
    
    private async Task<OutboundMessage?> ProcessMessageAsync(
        InboundMessage msg,
        CancellationToken cancellationToken,
        string? overrideSessionKey = null)
    {
        var sessionKey = overrideSessionKey ?? msg.SessionKey;
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionKey, cancellationToken);
        
        // ... 处理消息 ...
        
        // 内存整合检查（带重复防护）
        if (session.Messages.Count > _memoryWindow && !_consolidating.Contains(sessionKey))
        {
            _consolidating.Add(sessionKey);
            
            _ = Task.Run(async () =>
            {
                try
                {
                    await _memoryConsolidator.ConsolidateAsync(session.Messages, session.LastConsolidatedIndex);
                    session.LastConsolidatedIndex = session.Messages.Count - _memoryWindow / 2;
                }
                finally
                {
                    _consolidating.Remove(sessionKey);
                }
            }, cancellationToken);
        }
        
        // ... 返回响应 ...
    }
}
```

```csharp
// src/NanoBot.Core/Sessions/Session.cs

public class Session
{
    public string Key { get; set; } = string.Empty;
    public IList<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    public int LastConsolidatedIndex { get; set; } = 0;
    public List<string>? ToolsUsed { get; set; }
}
```

---

### 1.3 流式中间进度

**问题背景**: 工具执行期间用户无法看到进度，体验不佳。

**原项目实现**: `Temp/nanobot/nanobot/agent/loop.py:200-205`

**实现方案**:

```csharp
// src/NanoBot.Agent/AgentRuntime.cs

public interface IProgressReporter
{
    Task ReportProgressAsync(string content, CancellationToken cancellationToken = default);
}

public sealed class AgentRuntime : IAgentRuntime, IDisposable
{
    public async Task<AgentLoopResult> RunAgentLoopAsync(
        IList<ChatMessage> initialMessages,
        IProgressReporter? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        // ...
        
        if (response.ToolCalls != null && response.ToolCalls.Count > 0)
        {
            // 推送临时文本进度
            if (progressReporter != null)
            {
                var cleanText = StripThinkTags(responseMessage?.Text);
                if (!string.IsNullOrEmpty(cleanText))
                {
                    await progressReporter.ReportProgressAsync(cleanText, cancellationToken);
                }
                // 推送工具调用提示
                await progressReporter.ReportProgressAsync(FormatToolHint(response.ToolCalls), cancellationToken);
            }
            
            // 执行工具...
            foreach (var toolCall in response.ToolCalls)
            {
                var result = await ExecuteToolAsync(toolCall, cancellationToken);
                
                // 工具执行完成后也可推送进度
                if (progressReporter != null)
                {
                    var preview = result.Length > 100 ? result[..100] + "..." : result;
                    await progressReporter.ReportProgressAsync($"✓ {toolCall.Name}: {preview}", cancellationToken);
                }
            }
        }
    }
}
```

```csharp
// src/NanoBot.Infrastructure/Bus/BusProgressReporter.cs

public class BusProgressReporter : IProgressReporter
{
    private readonly IMessageBus _bus;
    private readonly string _channel;
    private readonly string _chatId;
    private readonly Dictionary<string, object>? _metadata;

    public BusProgressReporter(IMessageBus bus, string channel, string chatId, Dictionary<string, object>? metadata = null)
    {
        _bus = bus;
        _channel = channel;
        _chatId = chatId;
        _metadata = metadata;
    }

    public async Task ReportProgressAsync(string content, CancellationToken cancellationToken = default)
    {
        await _bus.PublishOutboundAsync(new OutboundMessage
        {
            Channel = _channel,
            ChatId = _chatId,
            Content = content,
            Metadata = _metadata
        }, cancellationToken);
    }
}
```

---

## 二、Shell 工具安全增强

### 2.1 进程超时后等待终止（防止 FD 泄漏）

**问题背景**: Shell 命令超时后，`process.Kill()` 被调用但进程未被等待，导致文件描述符泄漏。

**原项目实现**: `Temp/nanobot/nanobot/agent/tools/shell.py:84-90`

**实现方案**:

```csharp
// src/NanoBot.Tools/BuiltIn/Shell/ShellTools.cs

public static class ShellTools
{
    private static async Task<string> ExecuteAsync(string command, int timeoutSeconds, HashSet<string> blockedCommands)
    {
        try
        {
            // ... 安全检查 ...
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 30));
            using var process = new System.Diagnostics.Process { /* ... */ };

            process.Start();
            
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 关键修复：Kill 后等待进程完全终止
                process.Kill(entireProcessTree: true);
                
                try
                {
                    // 等待最多 5 秒让进程释放资源
                    using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(waitCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 进程在 5 秒内未终止，但已尽力
                }
                
                return $"Error: Command timed out after {timeoutSeconds} seconds";
            }

            var output = await outputTask;
            var error = await errorTask;
            
            // ... 返回结果 ...
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }
}
```

---

### 2.2 安全守卫增强

**问题背景**: 当前安全守卫过于简单，缺少完整的危险命令检测和路径限制。

**原项目实现**: `Temp/nanobot/nanobot/agent/tools/shell.py:118-150`

**实现方案**:

```csharp
// src/NanoBot.Tools/BuiltIn/Shell/ShellTools.cs

public class ShellToolOptions
{
    public int Timeout { get; set; } = 60;
    public string? WorkingDirectory { get; set; }
    public bool RestrictToWorkspace { get; set; } = false;
    public List<string>? AllowPatterns { get; set; }
    
    // 默认拒绝模式（参考原项目）
    public static readonly string[] DefaultDenyPatterns = 
    {
        @"\brm\s+-[rf]{1,2}\b",           // rm -r, rm -rf, rm -fr
        @"\bdel\s+/[fq]\b",               // del /f, del /q
        @"\brmdir\s+/s\b",                // rmdir /s
        @"(?:^|[;&|]\s*)format\b",        // format (仅作为独立命令)
        @"\b(mkfs|diskpart)\b",           // 磁盘操作
        @"\bdd\s+if=",                    // dd
        @">\s*/dev/sd",                   // 写入磁盘
        @"\b(shutdown|reboot|poweroff)\b", // 系统电源
        @":\(\)\s*\{.*\};\s*:",           // fork bomb
    };
}

public static class ShellTools
{
    public static AITool CreateExecTool(ShellToolOptions? options = null)
    {
        options ??= new ShellToolOptions();
        var denyPatterns = ShellToolOptions.DefaultDenyPatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase))
            .ToList();
        var allowPatterns = options.AllowPatterns?
            .Select(p => new Regex(p, RegexOptions.IgnoreCase))
            .ToList();
        
        return AIFunctionFactory.Create(
            (string command, string? workingDir) => ExecuteAsync(
                command, workingDir, options, denyPatterns, allowPatterns),
            new AIFunctionFactoryOptions
            {
                Name = "exec",
                Description = "Execute a shell command and return the output. Use with caution."
            });
    }
    
    private static string? GuardCommand(string command, string cwd, ShellToolOptions options, 
        List<Regex> denyPatterns, List<Regex>? allowPatterns)
    {
        var lower = command.ToLowerInvariant().Trim();
        
        // 检查拒绝模式
        foreach (var pattern in denyPatterns)
        {
            if (pattern.IsMatch(lower))
            {
                return "Error: Command blocked by safety guard (dangerous pattern detected)";
            }
        }
        
        // 检查允许列表
        if (allowPatterns != null && allowPatterns.Count > 0)
        {
            if (!allowPatterns.Any(p => p.IsMatch(lower)))
            {
                return "Error: Command blocked by safety guard (not in allowlist)";
            }
        }
        
        // 工作区限制
        if (options.RestrictToWorkspace)
        {
            // 检查路径遍历
            if (command.Contains("..\\") || command.Contains("../"))
            {
                return "Error: Command blocked by safety guard (path traversal detected)";
            }
            
            // 检查绝对路径是否在工作区内
            var workspacePath = Path.GetFullPath(options.WorkingDirectory ?? cwd);
            var absolutePaths = ExtractAbsolutePaths(command);
            
            foreach (var path in absolutePaths)
            {
                try
                {
                    var resolvedPath = Path.GetFullPath(path);
                    if (!resolvedPath.StartsWith(workspacePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return "Error: Command blocked by safety guard (path outside working directory)";
                    }
                }
                catch
                {
                    // 忽略无效路径
                }
            }
        }
        
        return null;
    }
    
    private static List<string> ExtractAbsolutePaths(string command)
    {
        var paths = new List<string>();
        
        // Windows 路径: C:\path
        var winMatches = Regex.Matches(command, @"[A-Za-z]:\\[^\\\"']+");
        foreach (Match m in winMatches)
        {
            paths.Add(m.Value);
        }
        
        // POSIX 路径: /path (仅匹配绝对路径)
        var posixMatches = Regex.Matches(command, @"(?:^|[\s|>])(/[^\s\"'>]+)");
        foreach (Match m in posixMatches)
        {
            paths.Add(m.Groups[1].Value);
        }
        
        return paths;
    }
    
    private static async Task<string> ExecuteAsync(
        string command, 
        string? workingDir,
        ShellToolOptions options,
        List<Regex> denyPatterns,
        List<Regex>? allowPatterns)
    {
        var cwd = workingDir ?? options.WorkingDirectory ?? Directory.GetCurrentDirectory();
        
        // 安全检查
        var guardError = GuardCommand(command, cwd, options, denyPatterns, allowPatterns);
        if (guardError != null)
        {
            return guardError;
        }
        
        // ... 执行命令 ...
        
        // 输出截断
        const int maxLen = 10000;
        if (result.Length > maxLen)
        {
            result = result[..maxLen] + $"\n... (truncated, {result.Length - maxLen} more chars)";
        }
        
        return result;
    }
}
```

---

## 三、Provider 实现补充

### 3.1 消息清理机制 (_sanitize_messages)

**问题背景**: 某些 LLM Provider（如 StepFun）对消息格式要求严格，非标准键（如 `reasoning_content`）会导致 400 错误。

**原项目实现**: `Temp/nanobot/nanobot/providers/litellm_provider.py:155-164`

**实现方案**:

```csharp
// src/NanoBot.Providers/MessageSanitizer.cs

public static class MessageSanitizer
{
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "role", "content", "tool_calls", "tool_call_id", "name"
    };
    
    public static IList<ChatMessage> SanitizeMessages(IList<ChatMessage> messages)
    {
        var sanitized = new List<ChatMessage>();
        
        foreach (var msg in messages)
        {
            // 创建新的干净消息
            var cleanContent = msg.Content;
            var cleanRole = msg.Role;
            
            // 对于 assistant 消息，确保有 content
            // 严格 Provider 要求即使只有 tool_calls 也要有 content 字段
            if (cleanRole == ChatRole.Assistant && string.IsNullOrEmpty(cleanContent))
            {
                cleanContent = string.Empty; // 或 null，取决于 Provider 要求
            }
            
            // 过滤工具调用中的非标准字段
            IList<AIToolCall>? cleanToolCalls = null;
            if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
            {
                cleanToolCalls = msg.ToolCalls.Select(tc => new AIToolCall(
                    tc.Id,
                    tc.Name,
                    tc.Arguments
                )).ToList();
            }
            
            sanitized.Add(new ChatMessage(cleanRole, cleanContent, cleanToolCalls));
        }
        
        return sanitized;
    }
}
```

```csharp
// src/NanoBot.Providers/ChatClientFactory.cs

public IChatClient CreateChatClient(string provider, string model, string? apiKey = null, string? apiBase = null)
{
    // ... 创建客户端 ...
    
    // 包装为清理客户端
    return new SanitizingChatClient(client, _logger);
}

// src/NanoBot.Providers/SanitizingChatClient.cs

public class SanitizingChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly ILogger? _logger;
    
    public SanitizingChatClient(IChatClient inner, ILogger? logger = null)
    {
        _inner = inner;
        _logger = logger;
    }
    
    public async Task<ChatResponse> CompleteAsync(IList<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var sanitized = MessageSanitizer.SanitizeMessages(messages);
        _logger?.LogDebug("Sanitized {Count} messages for strict provider", messages.Count);
        return await _inner.CompleteAsync(sanitized, options, cancellationToken);
    }
    
    // ... 其他方法委托给 _inner ...
}
```

---

### 3.2 Anthropic Prompt Caching

**问题背景**: Anthropic 支持 `cache_control` 进行提示缓存，可降低 API 成本。

**原项目实现**: `Temp/nanobot/nanobot/providers/litellm_provider.py:111-142`

**实现方案**:

```csharp
// src/NanoBot.Providers/CacheControl/CacheControlHelper.cs

public static class CacheControlHelper
{
    public static bool SupportsPromptCaching(string provider, string model)
    {
        return provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase);
    }
    
    public static IList<ChatMessage> ApplyCacheControl(IList<ChatMessage> messages, bool supportsCaching)
    {
        if (!supportsCaching) return messages;
        
        var result = new List<ChatMessage>();
        
        foreach (var msg in messages)
        {
            if (msg.Role == ChatRole.System)
            {
                // 为 system 消息添加 cache_control
                var content = msg.Content;
                if (content != null)
                {
                    // 创建带有 cache_control 的内容
                    // 注意：这需要自定义 ChatMessage 或使用 Provider 特定的扩展
                    var cachedMsg = new ChatMessage(ChatRole.System, content);
                    cachedMsg.Metadata = new Dictionary<string, object>
                    {
                        ["cache_control"] = new { type = "ephemeral" }
                    };
                    result.Add(cachedMsg);
                    continue;
                }
            }
            result.Add(msg);
        }
        
        return result;
    }
    
    public static IList<AITool>? ApplyCacheControlToTools(IList<AITool>? tools, bool supportsCaching)
    {
        if (!supportsCaching || tools == null || tools.Count == 0) return tools;
        
        var result = tools.ToList();
        
        // 为最后一个工具添加 cache_control
        var lastTool = result[^1];
        if (lastTool is AIFunction func)
        {
            // 创建带有 cache_control 的工具副本
            // 这需要自定义实现或使用 Provider 特定的扩展
        }
        
        return result;
    }
}
```

---

### 3.3 新增 Provider 支持

**VolcEngine Provider**:

```csharp
// src/NanoBot.Providers/ProviderSpecs.cs

public static class ProviderSpecs
{
    public static readonly Dictionary<string, ProviderSpec> All = new(StringComparer.OrdinalIgnoreCase)
    {
        // ... 现有 providers ...
        
        ["volcengine"] = new ProviderSpec(
            EnvKey: "VOLCENGINE_API_KEY",
            DefaultApiBase: "https://ark.cn-beijing.volces.com/api/v3",
            DisplayName: "VolcEngine",
            LiteLLMPrefix: "volcengine",
            Description: "火山引擎 LLM，支持 coding plan endpoint"
        ),
        
        ["siliconflow"] = new ProviderSpec(
            EnvKey: "SILICONFLOW_API_KEY",
            DefaultApiBase: "https://api.siliconflow.cn/v1",
            DisplayName: "SiliconFlow",
            LiteLLMPrefix: "siliconflow"
        ),
    };
}
```

---

## 四、Channel 实现补充

### 4.1 Telegram reply-to-message

**实现方案**:

```csharp
// src/NanoBot.Core/Configuration/Models/Channels/TelegramConfig.cs

public class TelegramConfig
{
    public string Token { get; set; } = string.Empty;
    public List<string> AllowFrom { get; set; } = new();
    
    // 新增：reply-to-message 配置
    public bool ReplyToMessage { get; set; } = false;
}
```

```csharp
// src/NanoBot.Channels/Implementations/Telegram/TelegramChannel.cs

public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
{
    // ... 现有代码 ...
    
    var replyToMessageId = GetReplyToMessageId(message);
    
    try
    {
        var html = MarkdownToTelegramHtml(chunk);
        await _botClient.SendTextMessageAsync(
            chatId, 
            html, 
            parseMode: ParseMode.Html, 
            replyToMessageId: replyToMessageId,  // 新增
            cancellationToken: cancellationToken);
    }
    // ...
}

private int? GetReplyToMessageId(OutboundMessage message)
{
    if (!_config.ReplyToMessage) return null;
    
    if (message.Metadata != null && 
        message.Metadata.TryGetValue("message_id", out var msgIdObj) && 
        msgIdObj is int msgId)
    {
        return msgId;
    }
    
    return null;
}
```

---

### 4.2 Telegram /help ACL 绕过

**实现方案**:

```csharp
// src/NanoBot.Channels/Implementations/Telegram/TelegramChannel.cs

private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
{
    // ... 获取消息 ...
    
    var content = string.Join("\n", contentParts);
    
    // 新增：/help 命令直接处理，绕过 ACL
    if (content.Trim().ToLowerInvariant() == "/help")
    {
        await SendHelpMessageAsync(chatId, cancellationToken);
        return;
    }
    
    // ACL 检查
    if (!IsAllowed(senderId, _config.AllowFrom))
    {
        _logger.LogWarning("Access denied for sender {SenderId} on Telegram channel", senderId);
        return;
    }
    
    // ... 继续处理 ...
}

private async Task SendHelpMessageAsync(string chatId, CancellationToken cancellationToken)
{
    if (_botClient == null || !long.TryParse(chatId, out var id)) return;
    
    var helpText = @"🐈 nanobot commands:
/new — Start a new conversation
/help — Show available commands";
    
    await _botClient.SendTextMessageAsync(id, helpText, cancellationToken: cancellationToken);
}
```

---

### 4.3 Feishu 多媒体发送

**实现方案**:

```csharp
// src/NanoBot.Channels/Implementations/Feishu/FeishuChannel.cs

public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
{
    if (string.IsNullOrEmpty(_accessToken)) return;
    
    // 处理媒体附件
    if (message.Attachments != null && message.Attachments.Count > 0)
    {
        foreach (var attachment in message.Attachments)
        {
            await SendMediaAsync(message.ChatId, attachment, cancellationToken);
        }
    }
    
    // 发送文本消息
    if (!string.IsNullOrEmpty(message.Content))
    {
        await SendTextMessageAsync(message.ChatId, message.Content, cancellationToken);
    }
}

private async Task SendMediaAsync(string chatId, MediaAttachment attachment, CancellationToken cancellationToken)
{
    var (msgType, uploadApi) = attachment.Type switch
    {
        MediaType.Image => ("image", "im/v1/images"),
        MediaType.Audio => ("file", "im/v1/files"),  // 音频作为文件发送
        MediaType.File => ("file", "im/v1/files"),
        _ => throw new NotSupportedException($"Media type {attachment.Type} not supported")
    };
    
    // 上传文件获取 file_key
    var fileKey = await UploadFileAsync(uploadApi, attachment, cancellationToken);
    
    // 发送消息
    var payload = new
    {
        receive_id_type = chatId.StartsWith("oc_") ? "chat_id" : "open_id",
        msg_type = msgType,
        content = JsonSerializer.Serialize(new { file_key }),
        receive_id = chatId
    };
    
    await SendApiRequestAsync("im/v1/messages", payload, cancellationToken);
}

private async Task<string> UploadFileAsync(string api, MediaAttachment attachment, CancellationToken cancellationToken)
{
    using var content = new MultipartFormDataContent();
    content.Add(new StringContent(attachment.FileName), "file_name");
    content.Add(new StringContent(chatId), "parent_type");
    content.Add(new ByteArrayContent(attachment.Data), "file", attachment.FileName);
    
    var response = await _httpClient.PostAsync(
        $"https://open.feishu.cn/open-apis/{api}",
        content,
        cancellationToken);
    
    var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    return result.GetProperty("data").GetProperty("file_key").GetString() 
        ?? throw new Exception("Failed to get file_key from upload response");
}
```

---

## 五、Cron 服务增强

### 5.1 时区验证

**实现方案**:

```csharp
// src/NanoBot.Infrastructure/Cron/CronService.cs

public class CronService : ICronService, IDisposable
{
    private void ValidateSchedule(CronSchedule schedule)
    {
        if (schedule.Kind == CronScheduleKind.Cron && !string.IsNullOrEmpty(schedule.TimeZone))
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
            }
            catch (TimeZoneNotFoundException)
            {
                throw new ArgumentException($"Invalid timezone: {schedule.TimeZone}");
            }
            catch (InvalidTimeZoneException ex)
            {
                throw new ArgumentException($"Invalid timezone: {schedule.TimeZone}", ex);
            }
        }
    }
    
    public CronJob AddJob(CronJobDefinition definition)
    {
        _lock.Wait();
        try
        {
            // 新增：验证时区
            ValidateSchedule(definition.Schedule);
            
            // ... 创建 job ...
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

---

## 六、MCP 实现补充

### 6.1 HTTP 自定义 Headers

**实现方案**:

```csharp
// src/NanoBot.Core/Configuration/Models/McpServerConfig.cs

public class McpServerConfig
{
    public string Command { get; set; } = string.Empty;
    public IReadOnlyList<string> Args { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Env { get; set; } = new();
    public string? Cwd { get; set; }
    
    // 新增：HTTP 自定义 Headers
    public Dictionary<string, string>? Headers { get; set; }
}
```

```csharp
// src/NanoBot.Tools/Mcp/McpClient.cs

public async Task ConnectAsync(string serverName, McpServerConfig config, CancellationToken cancellationToken = default)
{
    // ... 现有代码 ...
    
    // 处理自定义 Headers（用于 HTTP 传输）
    if (config.Headers != null && config.Headers.Count > 0)
    {
        // 如果使用 HTTP 传输，添加自定义 headers
        // 这取决于 MCP 客户端库的实现
        foreach (var (key, value) in config.Headers)
        {
            _logger?.LogInformation("MCP server '{ServerName}' using custom header: {Key}", serverName, key);
        }
    }
}
```

---

## 七、实现优先级与时间估算

| 优先级 | 功能 | 预估工作量 | 依赖 |
|--------|------|-----------|------|
| 🔴 P0 | Agent Loop 重试机制 | 4h | 无 |
| 🔴 P0 | Shell FD 泄漏修复 | 2h | 无 |
| 🔴 P0 | 消息清理机制 | 3h | 无 |
| 🟡 P1 | 内存整合重复防护 | 2h | 无 |
| 🟡 P1 | 流式中间进度 | 4h | Agent Loop |
| 🟡 P1 | Shell 安全守卫增强 | 4h | Shell FD 修复 |
| 🟡 P1 | Telegram /help ACL 绕过 | 1h | 无 |
| 🟡 P1 | 时区验证 | 1h | 无 |
| 🟢 P2 | Telegram reply-to-message | 2h | 无 |
| 🟢 P2 | Feishu 多媒体 | 6h | 无 |
| 🟢 P2 | Anthropic Prompt Caching | 4h | 无 |
| 🟢 P2 | 新增 Provider | 2h | 无 |
| 🟢 P2 | MCP HTTP Headers | 2h | 无 |

**总计**: 约 37 小时

---

## 八、测试要求

每个功能实现后必须通过以下测试：

1. **单元测试**: 覆盖核心逻辑
2. **集成测试**: 与其他模块的交互
3. **回归测试**: 确保现有功能不受影响
4. **性能测试**: 对于 Shell、Agent Loop 等关键路径

---

**文档版本**: 1.0  
**最后更新**: 2026-02-20
