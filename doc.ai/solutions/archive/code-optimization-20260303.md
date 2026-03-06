# NanoBot.Net 代码优化方案

**文档日期**: 2026-03-03
**基于**: 代码审核报告 + 原项目 nanobot 源码分析 + Microsoft.Agents.AI 框架分析

---

## 概述

本文档结合代码审核结果和原项目 nanobot 的设计理念，提出针对 NanoBot.Net 的优化方案。每个优化建议都包含问题描述、原项目参考、实现建议和预期收益。

---

## P0 - 紧急修复

### 1. 移除生产环境调试文件写入

**问题**: `SanitizingChatClient.GetStreamingResponseAsync` 每次请求写入调试文件

**原项目参考**: 原项目使用 `loguru` 日志框架，所有调试信息通过日志输出，不写入文件

**优化方案**:

```csharp
// 移除文件写入代码，改用条件化日志
#if DEBUG
var requestDir = Path.Combine(Path.GetTempPath(), "nanobot_requests");
// ... 调试代码
#endif

// 或使用日志级别 (见 P1 优化)
_logger?.LogDebug("Request: {MsgCount} messages, {TotalChars} chars", messageList.Count, totalChars);
```

**优先级**: P0 | **工作量**: 0.5h

---

### 2. 消除反射调用框架内部方法

**问题**: 多处反射调用 `Microsoft.Agents.AI` 私有方法

**原项目参考**: 原项目直接访问 `session.messages`，因为是简单的 Python 列表

**框架分析**: 
- 已分析 `Microsoft.Agents.AI` 源码 (`Temp/agent-framework/dotnet/src/Microsoft.Agents.AI.Abstractions/ChatHistoryProvider.cs`)
- 框架确实没有公开 `GetAllMessages` 方法
- 当前只能通过反射或自定义 ChatHistoryProvider 实现来获取消息

**优化方案**:

保留方案A - 封装为内部扩展方法:

```csharp
// 在 NanoBot.Agent 中创建扩展方法，集中管理反射调用
// 文件: NanoBot.Agent/Extensions/AgentSessionExtensions.cs

using System.Reflection;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

public static class AgentSessionExtensions
{
    private static readonly FieldInfo? MessagesField = typeof(ChatHistoryProvider)
        .GetField("_messages", BindingFlags.NonPublic | BindingFlags.Instance);

    public static IReadOnlyList<ChatMessage> GetAllMessages(this AgentSession session)
    {
        if (session == null)
            return Array.Empty<ChatMessage>();

        var provider = session.GetService<ChatHistoryProvider>();
        if (provider == null)
            return Array.Empty<ChatMessage>();

        // 通过反射获取消息
        if (MessagesField?.GetValue(provider) is IEnumerable<ChatMessage> messages)
            return messages.ToList();

        return Array.Empty<ChatMessage>();
    }
}

// 使用示例
var messages = session.GetAllMessages();
```

**附加说明**:
- 反射只在一处集中使用，便于维护
- 添加 TODO 注释跟踪框架更新
- 未来框架更新后可移除反射调用

**优先级**: P0 | **工作量**: 1h

---

## P1 - 高优先级优化

### 3. 简化 MessageBus 接口设计

**问题**: `IMessageBus` 职责过重，同时处理入站/出站消息

**原项目参考** (`nanobot/bus/queue.py`):
```python
class MessageBus:
    def __init__(self):
        self.inbound: asyncio.Queue[InboundMessage] = asyncio.Queue()
        self.outbound: asyncio.Queue[OutboundMessage] = asyncio.Queue()

    async def publish_inbound(self, msg: InboundMessage): ...
    async def consume_inbound(self) -> InboundMessage: ...
    async def publish_outbound(self, msg: OutboundMessage): ...
    async def consume_outbound(self) -> OutboundMessage: ...

    @property
    def inbound_size(self) -> int: ...
    @property
    def outbound_size(self) -> int: ...
```

**优化方案**:

保持当前接口设计，但简化实现:

```csharp
// 移除不必要的订阅机制，改为 ChannelManager 直接消费
public interface IMessageBus
{
    ValueTask PublishInboundAsync(InboundMessage message, CancellationToken ct = default);
    ValueTask<InboundMessage> ConsumeInboundAsync(CancellationToken ct = default);
    ValueTask PublishOutboundAsync(OutboundMessage message, CancellationToken ct = default);
    ValueTask<OutboundMessage> ConsumeOutboundAsync(CancellationToken ct = default);

    int InboundSize { get; }
    int OutboundSize { get; }
}

// 移除 SubscribeOutbound 方法，由 ChannelManager 直接调用 ConsumeOutboundAsync
```

**优先级**: P1 | **工作量**: 2h

---

### 4. 条件化调试日志 (NLog 配置优化)

**问题**: 大量 `[TIMING]`, `[DEBUG]`, `[PROMPT]` 日志

**原项目参考**: 原项目使用 `loguru`，可通过环境变量控制级别

**当前 NLog 配置** (`NanoBot.Cli/nlog.config`):
```xml
<rules>
    <logger name="*" minlevel="Info" writeTo="file"/>
</rules>
```

**优化方案**:

```xml
<!-- nlog.config - 生产环境使用 Warning 级别 -->
<rules>
    <!-- 开发环境: Debug 级别 -->
    <logger name="NanoBot" minlevel="Debug" writeTo="file"/>

    <!-- 生产环境: 将 minlevel 改为 Info 或 Warning -->
    <!-- <logger name="NanoBot" minlevel="Info" writeTo="file"/> -->
</rules>
```

**优先级**: P1 | **工作量**: 0.5h

---

### 5. Channel 基类重构

**问题**: 各 Channel 实现重复代码过多

**原项目参考** (`nanobot/channels/base.py`):
- 简洁的 `BaseChannel` 类
- 统一的 `_handle_message` 方法处理所有消息
- `is_allowed` 权限检查逻辑

**优化方案**:

```csharp
// ChannelBase.cs - 提取更多通用逻辑
public abstract class ChannelBase : IChannel
{
    protected readonly ILogger _logger;
    protected readonly IMessageBus Bus;
    protected bool _running;

    public abstract string Id { get; }
    public abstract string Type { get; }
    public bool IsConnected => _running;

    public event EventHandler<InboundMessage>? MessageReceived;

    // 模板方法模式 - 子类只需实现特定逻辑
    protected abstract Task<InboundMessage> CreateInboundMessageAsync(...);

    // 统一的连接生命周期管理
    public async Task StartAsync(CancellationToken ct)
    {
        await ConnectAsync(ct);
        _running = true;
        _ = ListenAsync(ct);  // 后台监听
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _running = false;
        await DisconnectAsync(ct);
    }

    // 统一的消息处理管道
    protected async Task HandleMessageAsync(...)
    {
        if (!IsAllowed(senderId)) return;

        var msg = await CreateInboundMessageAsync(...);
        await Bus.PublishInboundAsync(msg);
    }
}
```

**优先级**: P1 | **工作量**: 4h

---

## P2 - 中优先级优化

### 6. 统一配置验证

**问题**: 配置文件校验逻辑分散

**原项目参考**: 原项目在配置加载时验证 (`nanobot/config/loader.py`)

**优化方案**:

```csharp
// 统一的配置验证接口
public interface IConfigurationValidator
{
    ValidationResult Validate(AgentConfig config);
}

public class AgentConfigValidator : IConfigurationValidator
{
    public ValidationResult Validate(AgentConfig config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Llm.DefaultProfile))
            errors.Add("Default LLM profile is required");

        // ... 其他验证规则

        return new ValidationResult(errors);
    }
}
```

**优先级**: P2 | **工作量**: 2h

---

### 7. 消除 CLI 命令重复代码

**问题**: 每个命令类都创建独立的 ServiceCollection

**原项目参考**: 原项目使用 `click` 框架，命令共享同一个应用上下文

**优化方案**:

```csharp
// 创建共享的命令基础类
public abstract class NanoBotCommandBase : ICliCommand
{
    protected IServiceProvider? _serviceProvider;

    public void SetServiceProvider(IServiceProvider provider)
    {
        _serviceProvider = provider;
    }

    protected T GetService<T>() where T : notnull
    {
        return _serviceProvider!.GetRequiredService<T>();
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract Command CreateCommand();
}

// Program.cs - 集中配置
public static async Task<int> Main(string[] args)
{
    var services = new ServiceCollection();
    ConfigureServices(services);
    var provider = services.BuildServiceProvider();

    var commands = GetCommands();
    foreach (var cmd in commands)
    {
        if (cmd is NanoBotCommandBase baseCmd)
            baseCmd.SetServiceProvider(provider);
        rootCommand.AddCommand(cmd.CreateCommand());
    }
}
```

**优先级**: P2 | **工作量**: 3h

---

### 8. 简化配置加载逻辑

**问题**: ConfigurationLoader 300+ 行支持多种格式

**原项目参考**: 原项目只支持单一格式 (YAML)

**优化方案**:

分阶段废弃旧格式支持:

```csharp
// 阶段1: 标记旧方法为 Obsolete
[Obsolete("支持旧格式将在 2.0 版本移除，请使用 snake_case 格式")]
public static AgentConfig ConvertFromNanobotConfig(...)

// 阶段2: 只保留标准格式
public static AgentConfig Load(string configPath)
{
    var json = File.ReadAllText(configPath);
    return JsonSerializer.Deserialize<AgentConfig>(json, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    }) ?? throw new InvalidOperationException("Failed to deserialize");
}
```

**优先级**: P2 | **工作量**: 1h (分阶段)

---

### 9. 合并 Browser 服务接口

**问题**: 存在 `IBrowserService` 和 `IPlaywrightSessionManager` 两套接口

**优化方案**:

```csharp
// 保留一个统一接口
public interface IBrowserService
{
    Task<BrowserToolResponse> ExecuteAsync(BrowserToolRequest request, CancellationToken ct);
    Task<string> TakeSnapshotAsync(string? tabId = null);
    IReadOnlyList<BrowserTabInfo> GetTabs();
}

// 移除 IPlaywrightSessionManager，其功能合并到 BrowserService
```

**优先级**: P2 | **工作量**: 2h

---

### 10. 统一历史记录管理

**问题**: 消息历史被多次持久化 (SessionManager + FileBackedChatHistoryProvider)

**原项目参考**: 原项目会话和历史统一管理 (`nanobot/session/manager.py`)

**优化方案**:

```csharp
// 只保留一个存储位置: SessionManager
// FileBackedChatHistoryProvider 只负责内存中消息聚合
public class SessionManager
{
    // 消息同时保存到会话文件和可选的历史文件
    public async Task SaveSessionAsync(AgentSession session, string sessionKey)
    {
        // 序列化会话
        await SaveToFileAsync(sessionKey, session);

        // 可选: 追加到历史文件 (用于 grep 搜索)
        if (_config.Memory.PersistHistory)
        {
            await AppendToHistoryAsync(sessionKey, session.GetRecentMessages());
        }
    }
}
```

**优先级**: P2 | **工作量**: 2h

---

## P3 - 低优先级优化

### 11. 提取 Magic Strings

**问题**: 硬编码字符串散落各处

**优化方案**:

```csharp
public static class Constants
{
    public static class Bootstrap
    {
        public const string AgentsFile = "AGENTS.md";
        public const string SoulFile = "SOUL.md";
        public const string UserFile = "USER.md";
        public const string ToolsFile = "TOOLS.md";
        public const string IdentityFile = "IDENTITY.md";
    }

    public static class Commands
    {
        public const string New = "/new";
        public const string Help = "/help";
        public const string Stop = "/stop";
    }

    public static class EnvironmentVariables
    {
        public const string OpenAiApiKey = "OPENAI_API_KEY";
        public const string AnthropicApiKey = "ANTHROPIC_API_KEY";
        // ...
    }
}
```

---

### 12. 异步方法优化

**问题**: `ProvideChatHistoryAsync` 等方法标记为 async 但无真正异步操作

**优化方案**:

```csharp
// 如果没有真正的异步操作，使用同步实现
protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
    InvokingContext context,
    CancellationToken cancellationToken)
{
    // 同步操作直接返回
    if (context.Session?.StateBag.TryGetValue(StateKey, out var messages) == true)
    {
        return ValueTask.FromResult(messages);
    }
    return ValueTask.FromResult(Enumerable.Empty<ChatMessage>());
}
```

---

### 13. 大方法重构

**问题**: AgentCommand.ExecuteAgentAsync, ConfigCommand 等超过 400 行

**优化方案**:

将大型方法拆分为私有辅助方法:

```csharp
// ConfigCommand.cs 重构示例
private async Task ExecuteConfigAsync(...)
{
    if (interactive)
        await RunInteractiveLlmManagementAsync(...);
    else if (set != null)
        await SetConfigValuesAsync(...);
    else if (!string.IsNullOrEmpty(get))
        await GetConfigValueAsync(...);
    else
        await ListConfigAsync(...);
}

private async Task RunInteractiveLlmManagementAsync(...) { /* ... */ }
private async Task SetConfigValuesAsync(...) { /* ... */ }
private async Task GetConfigValueAsync(...) { /* ... */ }
private async Task ListConfigAsync(...) { /* ... */ }
```

---

## 实施计划

| 阶段 | 优先级 | 优化项 | 预计工作量 |
|------|--------|--------|-----------|
| 1 | P0 | 移除调试文件写入 | 0.5h |
| 1 | P0 | 消除反射调用 (方案A) | 1h |
| 2 | P1 | 简化 MessageBus | 2h |
| 2 | P1 | 条件化日志 (NLog) | 0.5h |
| 2 | P1 | Channel 重构 | 4h |
| 3 | P2 | 配置验证统一 | 2h |
| 3 | P2 | CLI 命令 DI | 3h |
| 3 | P2 | 配置加载简化 | 1h |
| 3 | P2 | Browser 接口合并 | 2h |
| 3 | P2 | 历史记录统一 | 2h |
| 4 | P3 | Magic Strings 提取 | 1h |
| 4 | P3 | 异步方法优化 | 1h |
| 4 | P3 | 大方法重构 | 3h |

---

## 测试方案

### 当前测试覆盖

项目已有 xUnit 测试框架，覆盖以下模块：

```
tests/
├── NanoBot.Agent.Tests/
│   ├── AgentRuntimeTests.cs
│   ├── SessionManagerTests.cs
│   ├── Context/ChatHistoryProviderTests.cs
│   └── Tools/SpawnToolTests.cs
├── NanoBot.Channels.Tests/
│   ├── ChannelManagerTests.cs
│   └── ChannelBaseTests.cs
├── NanoBot.Core.Tests/
│   └── Configuration/ConfigurationTests.cs
├── NanoBot.Infrastructure.Tests/
│   ├── Bus/MessageBusTests.cs
│   ├── Cron/CronServiceTests.cs
│   ├── Memory/MemoryConsolidatorTests.cs
│   └── Skills/SkillsLoaderTests.cs
├── NanoBot.Providers.Tests/
│   ├── ChatClientFactoryTests.cs
│   ├── InterimTextRetryChatClientTests.cs
│   └── MessageSanitizerTests.cs
└── NanoBot.Tools.Tests/
    ├── ToolsTests.cs
    └── ShellToolsSecurityTests.cs
```

### P0 优化测试方案

#### 1. 移除调试文件写入

**测试策略**: 验证文件不会在生产环境被创建

```csharp
// 新增测试文件: tests/NanoBot.Providers.Tests/DebugFileWriteTests.cs

public class DebugFileWriteTests
{
    [Fact]
    public void GetStreamingResponseAsync_DoesNotWriteFiles_InProduction()
    {
        // 验证 SanitizingChatClient 不再写入临时文件
        // 通过监控文件系统或检查代码是否包含文件写入逻辑
    }

    [Theory]
    [InlineData(true)]  // DEBUG
    [InlineData(false)] // RELEASE
    public void DebugFileBehavior_RespectsBuildConfiguration(bool isDebug)
    {
        // 验证 DEBUG 和 RELEASE 构建设置下行为正确
    }
}
```

#### 2. 消除反射调用

**测试策略**: 验证扩展方法正确工作

```csharp
// 新增测试文件: tests/NanoBot.Agent.Tests/AgentSessionExtensionsTests.cs

public class AgentSessionExtensionsTests
{
    [Fact]
    public void GetAllMessages_WithValidSession_ReturnsMessages()
    {
        // 测试从 AgentSession 获取消息列表
    }

    [Fact]
    public void GetAllMessages_WithNullSession_ReturnsEmptyList()
    {
        // 测试空会话返回空列表
    }

    [Fact]
    public void GetAllMessages_WithNoProvider_ReturnsEmptyList()
    {
        // 测试没有 ChatHistoryProvider 时返回空列表
    }
}
```

### P1 优化测试方案

#### 3. 简化 MessageBus

**测试策略**: 验证新接口兼容性

```csharp
// 扩展现有测试: tests/NanoBot.Infrastructure.Tests/Bus/MessageBusTests.cs

public class MessageBusSimplifiedTests
{
    [Fact]
    public void SubscribeOutbound_Removed_ChannelManagerUsesConsumeDirectly()
    {
        // 验证 ChannelManager 直接调用 ConsumeOutboundAsync
        // 不再依赖订阅机制
    }

    [Fact]
    public void MessageBus_StillImplementsIDisposable()
    {
        // 验证 Dispose 正常工作
    }
}
```

#### 4. NLog 配置

**测试策略**: 验证日志级别配置生效

```csharp
// 新增测试文件: tests/NanoBot.Cli.Tests/LoggingConfigurationTests.cs

public class LoggingConfigurationTests
{
    [Fact]
    public void NLogConfig_DebugLevel_WritesDetailedLogs()
    {
        // 验证 Debug 级别配置
    }

    [Fact]
    public void NLogConfig_WarningLevel_SuppressesInfoLogs()
    {
        // 验证 Warning 级别抑制 Info 日志
    }
}
```

#### 5. Channel 重构

**测试策略**: 验证模板方法模式正确工作

```csharp
// 扩展现有测试: tests/NanoBot.Channels.Tests/ChannelBaseTests.cs

public class ChannelBaseRefactoredTests
{
    [Fact]
    public void StartAsync_CallsConnectBeforeListen()
    {
        // 验证连接生命周期
    }

    [Fact]
    public void HandleMessageAsync_AppliesIsAllowedCheck()
    {
        // 验证权限检查
    }

    [Fact]
    public void CreateInboundMessage_ImplementedBySubclass()
    {
        // 验证子类正确实现模板方法
    }
}
```

### P2 优化测试方案

#### 6-10 各项优化

建议为每项优化添加对应的测试文件，确保重构不破坏现有功能：

```csharp
// 配置验证
public class ConfigurationValidatorTests
{
    [Fact]
    public void Validate_MissingLLMProfile_ReturnsError() { }
    [Fact]
    public void Validate_ValidConfig_ReturnsSuccess() { }
}

// CLI 依赖注入
public class CliCommandBaseTests
{
    [Fact]
    public void SetServiceProvider_AllowsServiceResolution() { }
}

// 配置加载
public class ConfigurationLoaderSimplifiedTests
{
    [Fact]
    public void Load_OnlySupportsSnakeCase() { }
}

// Browser 接口
public class BrowserServiceConsolidatedTests
{
    [Fact]
    public void SingleInterface_ContainsAllMethods() { }
}

// 历史记录
public class HistoryManagementTests
{
    [Fact]
    public void SaveSession_AlsoSavesToHistory_WhenEnabled() { }
}
```

---

## 总结

通过对比原项目 nanobot 的设计，我们发现：

1. **原项目更简洁** - MessageBus 只有两个队列，Channel 基类只有 119 行
2. **接口设计更清晰** - 职责分明，不过度抽象
3. **配置更统一** - 单一配置格式，验证集中

本优化方案遵循渐进式改进原则，优先修复影响生产环境的问题 (P0)，然后逐步优化架构设计 (P1-P3)。

**特别说明**:
- 日志系统已使用 NLog，可通过配置文件灵活控制日志级别，无需额外代码修改
- Microsoft.Agents.AI 框架目前未提供公开 API 获取所有消息，需保留反射方案但集中管理
