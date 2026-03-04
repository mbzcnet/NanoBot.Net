# NanoBot.Net 优化方案 - 补充篇

**文档日期**: 2026-03-03
**基于**: code-optimization-20260303.md 的补充

---

## P2-7: 消除 CLI 命令重复代码

### 问题分析

当前每个 CLI 命令类都创建独立的 `ServiceCollection`，导致：
- 重复的服务配置代码
- 无法共享已配置的服务实例
- 启动速度慢

### 优化方案

创建共享的命令基础类和统一服务配置：

```csharp
// 文件: src/NanoBot.Cli/Commands/NanoBotCommandBase.cs
public abstract class NanoBotCommandBase : ICliCommand
{
    protected static IServiceProvider? SharedServiceProvider { get; private set; }
    protected static AgentConfig? SharedConfig { get; private set; }

    public static void Initialize(IServiceProvider provider, AgentConfig config);

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract Command CreateCommand();

    protected T GetService<T>() where T : notnull;
    protected AgentConfig GetConfig();
}

// 文件: src/NanoBot.Cli/Program.cs - 修改后
public static async Task<int> Main(string[] args)
{
    var configPath = GetConfigPath(args);
    var config = await ConfigurationLoader.LoadWithDefaultsAsync(configPath);

    var services = new ServiceCollection();
    var configuration = BuildConfiguration(configPath);
    services.AddNanoBot(configuration);

    var provider = services.BuildServiceProvider();
    NanoBotCommandBase.Initialize(provider, config);

    // 注册所有命令
    var rootCommand = new RootCommand();
    // ...
    return await rootCommand.InvokeAsync(args);
}
```

### 预期收益

- 消除重复的服务配置代码
- 加快命令启动速度（共享服务实例）
- 统一配置加载逻辑

---

## P2-9: 合并 Browser 服务接口

### 问题分析

存在两套接口：
- `IBrowserService` - 面向工具层
- `IPlaywrightSessionManager` - 内部实现

功能重叠，职责不清。

### 优化方案

合并为一个统一接口：

```csharp
// 文件: src/NanoBot.Core/Tools/Browser/IBrowserService.cs
namespace NanoBot.Core.Tools.Browser;

public interface IBrowserService : IDisposable
{
    // 会话管理
    Task<bool> IsStartedAsync(string profile, CancellationToken ct = default);
    Task EnsureStartedAsync(string profile, CancellationToken ct = default);
    Task<BrowserToolResponse> StartAsync(string profile, CancellationToken ct = default);
    Task<BrowserToolResponse> StopAsync(string profile, CancellationToken ct = default);
    Task<BrowserToolResponse> StopAllAsync(CancellationToken ct = default);

    // Tab 管理
    Task<IReadOnlyList<BrowserTabInfo>> GetTabsAsync(string profile, CancellationToken ct = default);
    Task<BrowserToolResponse> OpenTabAsync(string url, string profile, CancellationToken ct = default);
    Task<BrowserToolResponse> CloseTabAsync(string targetId, string profile, CancellationToken ct = default);

    // 页面操作
    Task<BrowserToolResponse> NavigateAsync(string targetId, string url, string profile, CancellationToken ct = default);
    Task<BrowserToolResponse> GetSnapshotAsync(string targetId, string format, string profile, CancellationToken ct = default);
    Task<BrowserToolResponse> GetContentAsync(string targetId, string? selector, int? maxChars, string profile, CancellationToken ct = default);

    // 动作执行
    Task<BrowserToolResponse> ExecuteActionAsync(BrowserActionRequest request, string targetId, string profile, CancellationToken ct = default);

    // 状态查询
    Task<BrowserToolResponse> GetStatusAsync(string profile, CancellationToken ct = default);
}
```

### 预期收益

- 接口统一，职责清晰
- 减少类型转换和委托调用
- 简化依赖注入配置

---

## P2-10: 统一历史记录管理

### 问题分析

当前消息历史被多次持久化：
- `SessionManager` 保存会话文件
- `FileBackedChatHistoryProvider` 独立管理历史文件

### 优化方案

由 `SessionManager` 统一管理：

```csharp
// 文件: src/NanoBot.Agent/SessionManager.cs - 增强
public class SessionManager : ISessionManager
{
    private readonly AgentConfig _config;

    public Task SaveSessionAsync(AgentSession session, string sessionKey, CancellationToken ct = default);

    private Task SaveToFileAsync(string sessionKey, AgentSession session);
    private Task AppendToHistoryAsync(string sessionKey, AgentSession session);
    private string GetHistoryFilePath();
}

// 文件: src/NanoBot.Agent/Context/FileBackedChatHistoryProvider.cs - 简化
public class FileBackedChatHistoryProvider : IChatHistoryProvider
{
    public ValueTask ProvideChatHistoryAsync(InvokingContext context, CancellationToken ct);
    public ValueTask StoreChatMessageAsync(ChatMessage message, CancellationToken ct);
    public ValueTask ClearHistoryAsync(CancellationToken ct);
}
```

### 预期收益

- 单一职责：SessionManager 负责持久化，ChatHistoryProvider 负责内存聚合
- 减少重复 IO 操作
- 便于统一管理历史文件格式

---

## P3-11: 提取 Magic Strings

### 优化方案

创建常量类：

```csharp
// 文件: src/NanoBot.Core/Constants/AgentConstants.cs
namespace NanoBot.Core.Constants;

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
    public const string Clear = "/clear";
    public const string Exit = "/exit";
}

public static class EnvironmentVariables
{
    public const string OpenAiApiKey = "OPENAI_API_KEY";
    public const string AnthropicApiKey = "ANTHROPIC_API_KEY";
    public const string OpenRouterApiKey = "OPENROUTER_API_KEY";
}

public static class Channels
{
    public const string Telegram = "telegram";
    public const string Discord = "discord";
    public const string Slack = "slack";
    public const string WhatsApp = "whatsapp";
}
```

### 预期收益

- 消除魔法字符串
- 便于搜索和修改
- 统一命名规范

---

## P3-12: 异步方法优化

### 优化方案

对于没有真正异步操作的 `async` 方法，改为使用 `ValueTask`：

```csharp
// 文件: src/NanoBot.Agent/Context/FileBackedChatHistoryProvider.cs
protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
    InvokingContext context,
    CancellationToken cancellationToken);

// 文件: src/NanoBot.Agent/Context/MemoryConsolidationChatHistoryProvider.cs
protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
    InvokingContext context,
    CancellationToken cancellationToken);
```

### 预期收益

- 减少线程池资源消耗
- 降低上下文切换开销
- 提升低延迟场景性能

---

## P3-13: 大方法重构

### 问题分析

- `AgentCommand.ExecuteAgentAsync` - 500+ 行
- `ConfigCommand` - 400+ 行

### 优化方案

将大方法拆分为私有辅助方法：

```csharp
// 文件: src/NanoBot.Cli/Commands/ConfigCommand.cs
public class ConfigCommand : ICliCommand
{
    public Task ExecuteConfigAsync(
        string? get,
        string? set,
        bool list,
        bool interactive,
        CancellationToken ct);

    private Task RunInteractiveLlmManagementAsync(CancellationToken ct);
    private Task SetConfigValueAsync(string value, CancellationToken ct);
    private Task GetConfigValueAsync(string key, CancellationToken ct);
    private Task ListConfigAsync(CancellationToken ct);
    private Task ShowConfigHelpAsync(CancellationToken ct);
}

// 文件: src/NanoBot.Cli/Commands/AgentCommand.cs
public async Task<int> ExecuteAgentAsync(...);

private Task<AgentConfig> LoadConfigAsync(...);
private IAgentRuntime CreateRuntime(AgentConfig config);
private Task InitializeRuntimeAsync(IAgentRuntime runtime, AgentConfig config, CancellationToken ct);
private Task<string?> ReadInputAsync(CancellationToken ct);
private bool ShouldExit(string? message);
private Task ProcessMessageAsync(IAgentRuntime runtime, string message, CancellationToken ct);
private Task CleanupAsync(IAgentRuntime runtime);
```

### 预期收益

- 代码可读性提升
- 便于单元测试（可单独测试辅助方法）
- 更容易发现重复代码

---

## 实施优先级

| 优先级 | 优化项 | 工作量 | 依赖 |
|--------|--------|--------|------|
| P2 | P2-7 CLI 命令 DI | 3h | P2-6 |
| P2 | P2-9 Browser 接口合并 | 2h | 无 |
| P2 | P2-10 历史记录管理 | 2h | P0-2 |
| P3 | P3-11 Magic Strings | 1h | 无 |
| P3 | P3-12 异步优化 | 1h | P1 |
| P3 | P3-13 大方法重构 | 3h | 无 |

---

## 测试方案

每项优化需添加对应测试：

```csharp
// CLI DI 测试
public class NanoBotCommandBaseTests
{
    [Fact]
    public void Initialize_SetsSharedProvider();

    [Fact]
    public void GetService_ResolvesFromSharedProvider();
}

// Browser 接口测试
public class BrowserServiceConsolidatedTests
{
    [Fact]
    public void SingleInterface_ContainsAllMethods();
}

// 历史记录测试
public class HistoryManagementTests
{
    [Fact]
    public void SaveSession_AlsoSavesToHistory_WhenEnabled();
}
```
