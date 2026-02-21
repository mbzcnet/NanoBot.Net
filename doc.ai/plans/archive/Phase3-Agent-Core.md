# Phase 3: Agent 核心层实现计划（基于 Microsoft.Agents.AI）

本阶段实现 NanoBot.Net 的 Agent 核心层，**直接使用 Microsoft.Agents.AI 框架提供的 `ChatClientAgent`、`AIContextProvider` 和 `ChatHistoryProvider`**，避免重复造轮子。

## 阶段目标

基于 Microsoft.Agents.AI 框架实现 Agent 运行时，通过框架提供的类型实现多轮对话、工具调用、记忆管理和会话管理。

## 核心原则

### 框架已提供（不需要实现）

| 功能 | 框架类型 | 说明 |
|------|----------|------|
| Agent 基类 | `ChatClientAgent` | Agent 实现，支持 RunAsync/RunStreamingAsync |
| 工具调用循环 | 框架自动处理 | 通过 `FunctionInvokingChatClient` 自动处理 |
| 会话管理 | `AgentSession` | 会话状态管理，支持序列化/反序列化 |
| 中间件 | `AIAgentBuilder` | Agent 管道构建 |
| 上下文注入 | `AIContextProvider` | 动态上下文注入，调用前后均可介入 |
| 历史管理 | `ChatHistoryProvider` | 对话历史管理 |
| Agent 协作 | `AsAIFunction()` | 将 Agent 转换为工具 |

### 需要实现

| 功能 | 说明 | 原因 |
|------|------|------|
| NanoBotAgentFactory | 创建 ChatClientAgent | 整合 nanobot 配置 |
| FileBackedChatHistoryProvider | 管理 HISTORY.md | nanobot 特有 |
| BootstrapContextProvider | 加载 AGENTS.md、SOUL.md | nanobot 特有 |
| MemoryContextProvider | 加载/更新 MEMORY.md | nanobot 特有 |
| SkillsContextProvider | 加载 Skills | nanobot 特有 |
| IMemoryStore | 记忆读写接口 | nanobot 特有 |
| AgentRuntime | 消息总线处理 | nanobot 特有 |

---

## 相关方案文档

- [Agent-Core.md](../solutions/Agent-Core.md) - Agent 核心层设计
- [Overview.md](../solutions/Overview.md) - 框架集成策略

## 阶段依赖

- Phase 1-2 重构已完成
- Microsoft.Agents.AI 包已引用
- `IChatClient` 工厂可用
- 工具以 `AITool` 形式注册
- 消息总线可用
- Workspace 管理可用

---

## 任务清单概览

| 任务清单 | 主要内容 | 并行度 |
|----------|----------|--------|
| [Provider 模块](#任务清单-provider-模块) | ChatHistoryProvider 和 AIContextProvider 实现 | 高 |
| [记忆管理模块](#任务清单-记忆管理模块) | IMemoryStore 实现 | 高 |
| [Agent 工厂模块](#任务清单-agent-工厂模块) | ChatClientAgent 创建 | 中 |
| [Agent 运行时模块](#任务清单-agent-运行时模块) | 消息处理循环 | 中 |

---

## 任务清单：Provider 模块

### 任务目标

实现框架的 `ChatHistoryProvider` 和 `AIContextProvider` 抽象，注入 nanobot 特有的上下文。

### 相关方案文档

- [Agent-Core.md](../solutions/Agent-Core.md) - Provider 实现

### 任务依赖

- Workspace 管理模块
- Skills 加载模块

### 任务列表

#### Task 3.1.1: 实现 FileBackedChatHistoryProvider ✅

**描述**: 实现基于文件的对话历史提供者，管理 HISTORY.md。

**状态**: 已完成

**交付物**:
- `NanoBot.Agent/Context/FileBackedChatHistoryProvider.cs` 文件
- 继承 `ChatHistoryProvider`
- 实现 `ProvideChatHistoryAsync` 和 `StoreChatHistoryAsync`

**完成标准**:
- 正确继承 `ChatHistoryProvider`
- 框架自动调用提供和存储方法
- 支持历史条目数量限制

**示例代码**:
```csharp
public class FileBackedChatHistoryProvider : ChatHistoryProvider
{
    private readonly IWorkspaceManager _workspace;
    private readonly int _maxHistoryEntries;

    public FileBackedChatHistoryProvider(
        IWorkspaceManager workspace,
        int maxHistoryEntries = 100)
    {
        _workspace = workspace;
        _maxHistoryEntries = maxHistoryEntries;
    }

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var historyPath = _workspace.GetFilePath("HISTORY.md");
        if (!File.Exists(historyPath))
            return [];

        var lines = await File.ReadAllLinesAsync(historyPath, cancellationToken);
        return ParseHistoryToMessages(lines.TakeLast(_maxHistoryEntries));
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
        var historyPath = _workspace.GetFilePath("HISTORY.md");
        var sb = new StringBuilder();
        
        foreach (var message in context.RequestMessages)
            sb.AppendLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message.Role}: {message.Text}");
        
        foreach (var message in context.ResponseMessages)
            sb.AppendLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message.Role}: {message.Text}");
        
        await File.AppendAllTextAsync(historyPath, sb.ToString(), cancellationToken);
    }
}
```

---

#### Task 3.1.2: 实现 BootstrapContextProvider ✅

**描述**: 实现 Bootstrap 文件上下文提供者。

**状态**: 已完成

**交付物**:
- `NanoBot.Agent/Context/BootstrapContextProvider.cs` 文件
- 继承 `AIContextProvider`
- 实现 `ProvideAIContextAsync`

**完成标准**:
- 正确继承 `AIContextProvider`
- 使用正确的方法签名 `ProvideAIContextAsync`
- 返回 `AIContext` 包含 Instructions

**示例代码**:
```csharp
public class BootstrapContextProvider : AIContextProvider
{
    private readonly IWorkspaceManager _workspace;

    public BootstrapContextProvider(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var instructions = new StringBuilder();

        var agentsPath = _workspace.GetFilePath("AGENTS.md");
        if (File.Exists(agentsPath))
        {
            instructions.AppendLine("## Agent Configuration");
            instructions.AppendLine(await File.ReadAllTextAsync(agentsPath, cancellationToken));
        }

        var soulPath = _workspace.GetFilePath("SOUL.md");
        if (File.Exists(soulPath))
        {
            instructions.AppendLine("## Personality");
            instructions.AppendLine(await File.ReadAllTextAsync(soulPath, cancellationToken));
        }

        return new AIContext
        {
            Instructions = instructions.Length > 0 ? instructions.ToString() : null
        };
    }
}
```

---

#### Task 3.1.3: 实现 MemoryContextProvider ✅

**描述**: 实现记忆上下文提供者，支持调用后更新记忆。

**状态**: 已完成

**交付物**:
- `NanoBot.Agent/Context/MemoryContextProvider.cs` 文件
- 实现 `ProvideAIContextAsync` 和 `StoreAIContextAsync`

**完成标准**:
- 调用前加载 MEMORY.md
- 调用后更新记忆

**示例代码**:
```csharp
public class MemoryContextProvider : AIContextProvider
{
    private readonly IMemoryStore _memoryStore;

    public MemoryContextProvider(IMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var memory = await _memoryStore.LoadAsync(cancellationToken);
        
        return string.IsNullOrEmpty(memory) 
            ? new AIContext() 
            : new AIContext { Instructions = $"## Memory\n{memory}" };
    }

    protected override async ValueTask StoreAIContextAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
        await _memoryStore.UpdateAsync(
            context.RequestMessages,
            context.ResponseMessages,
            cancellationToken);
    }
}
```

---

#### Task 3.1.4: 实现 SkillsContextProvider ✅

**描述**: 实现 Skills 上下文提供者。

**状态**: 已完成

**交付物**:
- `NanoBot.Agent/Context/SkillsContextProvider.cs` 文件
- 加载 Skills 摘要

**完成标准**:
- 正确加载 Skills
- 支持 always=true 的完整加载

---

#### Task 3.1.5: 编写 Provider 测试 ✅

**描述**: 编写 Provider 的单元测试。

**状态**: 已完成

**交付物**:
- `NanoBot.Agent.Tests/Context/ProviderTests.cs` 文件

**完成标准**:
- 测试覆盖率 >= 80%
- 所有测试通过

### 成功指标

- 所有 Provider 正确实现
- 框架正确调用 Provider
- 单元测试覆盖率 >= 80%

---

## 任务清单：记忆管理模块

### 任务目标

实现记忆存储接口，管理 MEMORY.md 文件。

### 相关方案文档

- [Agent-Core.md](../solutions/Agent-Core.md) - IMemoryStore 接口

### 任务依赖

- Workspace 管理模块

### 任务列表

#### Task 3.2.1: 定义 IMemoryStore 接口

**描述**: 定义记忆存储接口。

**交付物**:
- `NanoBot.Core/Memory/IMemoryStore.cs` 接口文件
- LoadAsync 和 UpdateAsync 方法声明

**完成标准**:
- 接口定义与设计文档一致

**示例代码**:
```csharp
public interface IMemoryStore
{
    Task<string> LoadAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(
        IEnumerable<ChatMessage> requestMessages,
        IEnumerable<ChatMessage> responseMessages,
        CancellationToken cancellationToken = default);
}
```

---

#### Task 3.2.2: 实现 MemoryStore 类

**描述**: 实现记忆存储。

**交付物**:
- `NanoBot.Infrastructure/Memory/MemoryStore.cs` 实现文件
- MEMORY.md 读写逻辑

**完成标准**:
- 正确读写记忆文件
- 线程安全

---

#### Task 3.2.3: 实现记忆合并逻辑

**描述**: 实现会话消息合并到记忆的逻辑。

**交付物**:
- 记忆合并方法实现
- LLM 总结调用（可选）

**完成标准**:
- 正确识别需要合并的消息
- 生成合理的记忆更新

---

#### Task 3.2.4: 编写记忆模块测试

**描述**: 编写记忆模块的单元测试。

**交付物**:
- `NanoBot.Infrastructure.Tests/Memory/MemoryStoreTests.cs` 文件

**完成标准**:
- 测试覆盖率 >= 80%
- 所有测试通过

### 成功指标

- 记忆读写正确
- 单元测试覆盖率 >= 80%

---

## 任务清单：Agent 工厂模块

### 任务目标

创建 `ChatClientAgent` 实例，整合 nanobot 配置。

### 相关方案文档

- [Agent-Core.md](../solutions/Agent-Core.md) - NanoBotAgentFactory 实现

### 任务依赖

- Provider 模块
- 记忆管理模块
- IChatClient 工厂
- 工具列表

### 任务列表

#### Task 3.3.1: 实现 NanoBotAgentFactory ✅

**描述**: 实现 Agent 工厂，创建配置好的 ChatClientAgent。

**状态**: 已完成

**交付物**:
- `NanoBot.Agent/NanoBotAgentFactory.cs` 文件
- 使用 `ChatClientAgent` 构造函数
- 注入 Providers

**完成标准**:
- ✅ 正确创建 `ChatClientAgent`
- ✅ 支持同步和流式响应

**示例代码**:
```csharp
public static class NanoBotAgentFactory
{
    public static ChatClientAgent Create(
        IChatClient chatClient,
        IWorkspaceManager workspace,
        ISkillsLoader skillsLoader,
        IReadOnlyList<AITool> tools,
        ILoggerFactory loggerFactory)
    {
        var contextProviders = new List<AIContextProvider>
        {
            new BootstrapContextProvider(workspace),
            new MemoryContextProvider(workspace),
            new SkillsContextProvider(skillsLoader)
        };

        var historyProvider = new FileBackedChatHistoryProvider(workspace);
        var instructions = BuildInstructions(workspace);

        return new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = "NanoBot",
                Description = "A personal AI assistant",
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions,
                    Tools = tools
                },
                AIContextProviders = contextProviders,
                ChatHistoryProvider = historyProvider
            },
            loggerFactory);
    }
}
```

---

#### Task 3.3.2: 实现指令构建逻辑 ✅

**描述**: 实现动态构建 Agent 指令的逻辑。

**状态**: 已完成

**交付物**:
- `BuildInstructions` 方法实现
- 加载 AGENTS.md、SOUL.md

**完成标准**:
- ✅ 正确构建系统指令
- ✅ 支持动态更新

---

#### Task 3.3.3: 实现 spawn 工具 ✅

**描述**: 使用 `AsAIFunction()` 实现子 Agent 创建。

**状态**: 已完成

**交付物**:
- `NanoBot.Agent/Tools/SpawnTool.cs` 文件
- 使用 `AIFunctionFactory.Create`

**完成标准**:
- ✅ 正确创建子 Agent
- ✅ 正确返回结果

**示例代码**:
```csharp
public static AITool CreateSpawnTool(
    IChatClient chatClient,
    ILogger logger)
{
    [Description("Create a sub-agent to handle a specific task.")]
    async Task<string> SpawnAsync(
        [Description("The task")] string task,
        [Description("Optional label")] string? label = null)
    {
        var subAgentName = label ?? $"subagent_{Guid.NewGuid():N}";
        
        var subAgent = new ChatClientAgent(
            chatClient,
            instructions: $"You are a specialized agent. Task: {task}",
            name: subAgentName);
        
        var response = await subAgent.RunAsync(task);
        
        return $"Sub-agent {subAgentName} completed:\n{response.Text}";
    }

    return AIFunctionFactory.Create(SpawnAsync, new AIFunctionFactoryOptions
    {
        Name = "spawn",
        Description = "Create a sub-agent to handle a specific task."
    });
}
```

---

#### Task 3.3.4: 编写 Agent 工厂测试 ✅

**描述**: 编写 Agent 工厂的单元测试。

**状态**: 已完成

**交付物**:
- `NanoBot.Agent.Tests/NanoBotAgentFactoryTests.cs` 文件

**完成标准**:
- ✅ 测试覆盖率 >= 80%
- ✅ 所有测试通过

### 成功指标

- ✅ ChatClientAgent 正确创建
- ✅ 支持同步和流式响应
- ✅ 子 Agent 创建正确

---

## 任务清单：Agent 运行时模块

### 任务目标

实现 Agent 运行时，处理消息总线的消息。

### 相关方案文档

- [Agent-Core.md](../solutions/Agent-Core.md) - Agent 运行时

### 任务依赖

- Agent 工厂模块
- 消息总线
- 通道管理器

### 任务列表

#### Task 3.4.1: 实现 AgentRuntime 类

**描述**: 实现 Agent 运行时，监听消息总线。

**交付物**:
- `NanoBot.Agent/AgentRuntime.cs` 文件
- 订阅入站消息
- 调用 ChatClientAgent

**完成标准**:
- 正确监听消息总线
- 正确调用 Agent

---

#### Task 3.4.2: 实现会话管理

**描述**: 使用框架的 `AgentSession` 管理会话。

**交付物**:
- 会话创建逻辑
- 会话持久化逻辑（使用框架序列化）

**完成标准**:
- 正确创建和获取会话
- 使用 `SerializeSessionAsync`/`DeserializeSessionAsync` 持久化

**示例代码**:
```csharp
public class SessionManager
{
    private readonly ChatClientAgent _agent;
    private readonly string _sessionDir;

    public async Task<AgentSession> GetOrCreateSessionAsync(string sessionId)
    {
        var sessionFile = Path.Combine(_sessionDir, $"{sessionId}.json");
        
        if (File.Exists(sessionFile))
        {
            var json = await File.ReadAllTextAsync(sessionFile);
            return await _agent.DeserializeSessionAsync(JsonDocument.Parse(json).RootElement);
        }
        
        return await _agent.CreateSessionAsync();
    }

    public async Task SaveSessionAsync(AgentSession session, string sessionId)
    {
        var serialized = await _agent.SerializeSessionAsync(session);
        var sessionFile = Path.Combine(_sessionDir, $"{sessionId}.json");
        await File.WriteAllTextAsync(sessionFile, serialized.GetRawText());
    }
}
```

---

#### Task 3.4.3: 实现消息处理流程

**描述**: 实现完整的消息处理流程。

**交付物**:
- 入站消息处理
- 出站消息发送
- 错误处理

**完成标准**:
- 正确处理消息
- 正确发送响应

---

#### Task 3.4.4: 编写运行时测试

**描述**: 编写运行时的单元测试。

**交付物**:
- `NanoBot.Agent.Tests/AgentRuntimeTests.cs` 文件

**完成标准**:
- 测试覆盖率 >= 80%
- 所有测试通过

### 成功指标

- 消息处理流程正确
- 会话管理正确

---

## 项目目录结构

```
src/
├── NanoBot.Core/
│   └── Memory/
│       └── IMemoryStore.cs
│
├── NanoBot.Agent/
│   ├── NanoBotAgentFactory.cs
│   ├── AgentRuntime.cs
│   ├── Context/
│   │   ├── FileBackedChatHistoryProvider.cs
│   │   ├── BootstrapContextProvider.cs
│   │   ├── MemoryContextProvider.cs
│   │   └── SkillsContextProvider.cs
│   └── Tools/
│       └── SpawnTool.cs
│
├── NanoBot.Infrastructure/
│   └── Memory/
│       └── MemoryStore.cs
│
└── tests/
    ├── NanoBot.Agent.Tests/
    │   ├── NanoBotAgentFactoryTests.cs
    │   ├── AgentRuntimeTests.cs
    │   └── Context/
    │       └── ProviderTests.cs
    └── NanoBot.Infrastructure.Tests/
        └── Memory/
            └── MemoryStoreTests.cs
```

---

## 风险评估

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|----------|
| 框架 API 变更 | 高 | 低 | 使用稳定版本，关注更新日志 |
| 上下文注入失败 | 中 | 低 | 完善错误处理，日志记录 |
| 会话持久化问题 | 中 | 低 | 使用框架内置序列化能力 |

---

## 阶段完成标准

- [x] FileBackedChatHistoryProvider 实现完成
- [x] 所有 AIContextProvider 实现完成
- [x] MemoryStore 实现完成
- [x] NanoBotAgentFactory 实现完成
- [x] spawn 工具实现完成
- [x] AgentRuntime 实现完成
- [x] 所有单元测试通过
- [x] Agent 循环可正常运行

## 下一阶段

完成本阶段后，进入 [Phase 4: 应用层](./Phase4-Application.md)。
