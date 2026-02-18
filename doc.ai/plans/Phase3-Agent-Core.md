# Phase 3: Agent 核心层实现计划（基于 Microsoft.Agents.AI）

本阶段实现 NanoBot.Net 的 Agent 核心层，**直接使用 Microsoft.Agents.AI 框架提供的 `ChatClientAgent` 和 `AIContextProvider`**，避免重复造轮子。

## 阶段目标

基于 Microsoft.Agents.AI 框架实现 Agent 运行时，通过框架提供的类型实现多轮对话、工具调用、记忆管理和会话管理。

## 核心原则

### 框架已提供（不需要实现）

| 功能 | 框架类型 | 说明 |
|------|----------|------|
| Agent 基类 | `ChatClientAgent` | Agent 实现，支持 RunAsync/RunStreamingAsync |
| 工具调用循环 | 框架自动处理 | 无需手动实现循环 |
| 会话管理 | `AgentSession`/`AgentThread` | 会话状态管理 |
| 中间件 | `AIAgentBuilder` | Agent 管道构建 |
| 上下文注入 | `AIContextProvider` | 动态上下文注入 |

### 需要实现

| 功能 | 说明 | 原因 |
|------|------|------|
| NanoBotAgent | 封装 ChatClientAgent | 整合 nanobot 特有功能 |
| BootstrapContextProvider | 加载 AGENTS.md、SOUL.md | nanobot 特有 |
| MemoryContextProvider | 加载 MEMORY.md | nanobot 特有 |
| HistoryContextProvider | 加载 HISTORY.md | nanobot 特有 |
| SkillsContextProvider | 加载 Skills | nanobot 特有 |
| MemoryStore | 记忆读写 | nanobot 特有 |

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
| [上下文提供者模块](#任务清单-上下文提供者模块) | AIContextProvider 实现 | 高 |
| [记忆管理模块](#任务清单-记忆管理模块) | IMemoryStore 实现 | 高 |
| [NanoBotAgent 模块](#任务清单-nanobotagent-模块) | Agent 封装实现 | 中 |
| [Agent 运行时模块](#任务清单-agent-运行时模块) | 消息处理循环 | 中 |

---

## 任务清单：上下文提供者模块

### 任务目标

实现框架的 `AIContextProvider` 抽象，注入 nanobot 特有的上下文（Bootstrap、Memory、Skills）。

### 相关方案文档

- [Agent-Core.md](../solutions/Agent-Core.md) - AIContextProvider 实现

### 任务依赖

- Workspace 管理模块
- Skills 加载模块

### 任务列表

#### Task 3.1.1: 实现 BootstrapContextProvider

**描述**: 实现 Bootstrap 文件上下文提供者。

**交付物**:
- `NanoBot.Agent/Context/BootstrapContextProvider.cs` 文件
- 继承 `AIContextProvider`
- 加载 AGENTS.md、SOUL.md

**完成标准**:
- 正确继承 `AIContextProvider`
- 动态加载 Bootstrap 文件
- 返回 `AIContext`

**示例代码**:
```csharp
public class BootstrapContextProvider : AIContextProvider
{
    private readonly IWorkspaceManager _workspace;
    
    public BootstrapContextProvider(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }
    
    protected override async Task<AIContext> GetContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var aiContext = new AIContext();
        
        var agentsPath = _workspace.GetFilePath("AGENTS.md");
        if (File.Exists(agentsPath))
        {
            aiContext.AdditionalData["agents"] = await File.ReadAllTextAsync(agentsPath, cancellationToken);
        }
        
        var soulPath = _workspace.GetFilePath("SOUL.md");
        if (File.Exists(soulPath))
        {
            aiContext.AdditionalData["soul"] = await File.ReadAllTextAsync(soulPath, cancellationToken);
        }
        
        return aiContext;
    }
}
```

---

#### Task 3.1.2: 实现 MemoryContextProvider

**描述**: 实现记忆上下文提供者。

**交付物**:
- `NanoBot.Agent/Context/MemoryContextProvider.cs` 文件
- 加载 MEMORY.md

**完成标准**:
- 正确加载记忆文件
- 返回 `AIContext`

---

#### Task 3.1.3: 实现 HistoryContextProvider

**描述**: 实现历史上下文提供者。

**交付物**:
- `NanoBot.Agent/Context/HistoryContextProvider.cs` 文件
- 加载 HISTORY.md（最近 N 条）

**完成标准**:
- 正确加载历史文件
- 支持条目数量限制

---

#### Task 3.1.4: 实现 SkillsContextProvider

**描述**: 实现 Skills 上下文提供者。

**交付物**:
- `NanoBot.Agent/Context/SkillsContextProvider.cs` 文件
- 加载 Skills 摘要

**完成标准**:
- 正确加载 Skills
- 支持 always=true 的完整加载

---

#### Task 3.1.5: 编写上下文提供者测试

**描述**: 编写上下文提供者的单元测试。

**交付物**:
- `NanoBot.Agent.Tests/Context/ContextProviderTests.cs` 文件

**完成标准**:
- 测试覆盖率 >= 80%
- 所有测试通过

### 成功指标

- 所有 ContextProvider 正确实现
- 框架正确调用 ContextProvider
- 单元测试覆盖率 >= 80%

---

## 任务清单：记忆管理模块

### 任务目标

实现记忆存储接口，管理 MEMORY.md 和 HISTORY.md 文件。

### 相关方案文档

- [Agent-Core.md](../solutions/Agent-Core.md) - IMemoryStore 接口

### 任务依赖

- Workspace 管理模块

### 任务列表

#### Task 3.2.1: 定义 IMemoryStore 接口

**描述**: 定义记忆存储接口。

**交付物**:
- `NanoBot.Core/Memory/IMemoryStore.cs` 接口文件
- 读写方法声明

**完成标准**:
- 接口定义与设计文档一致

---

#### Task 3.2.2: 实现 MemoryStore 类

**描述**: 实现记忆存储。

**交付物**:
- `NanoBot.Infrastructure/Memory/MemoryStore.cs` 实现文件
- MEMORY.md 读写逻辑
- HISTORY.md 追加逻辑

**完成标准**:
- 正确读写记忆文件
- 线程安全

---

#### Task 3.2.3: 实现记忆合并逻辑

**描述**: 实现会话消息合并到记忆的逻辑。

**交付物**:
- 记忆合并方法实现
- LLM 总结调用

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
- 历史追加正确
- 单元测试覆盖率 >= 80%

---

## 任务清单：NanoBotAgent 模块

### 任务目标

封装 `ChatClientAgent`，整合 nanobot 特有功能。

### 相关方案文档

- [Agent-Core.md](../solutions/Agent-Core.md) - NanoBotAgent 实现

### 任务依赖

- 上下文提供者模块
- 记忆管理模块
- IChatClient 工厂
- 工具列表

### 任务列表

#### Task 3.3.1: 实现 NanoBotAgent 类

**描述**: 实现 NanoBotAgent，封装 ChatClientAgent。

**交付物**:
- `NanoBot.Agent/NanoBotAgent.cs` 文件
- 使用 `ChatClientAgent` 作为内部实现
- 注入 ContextProviders

**完成标准**:
- 正确封装 `ChatClientAgent`
- 支持同步和流式响应

**示例代码**:
```csharp
public class NanoBotAgent
{
    private readonly ChatClientAgent _innerAgent;
    
    public NanoBotAgent(
        IChatClient chatClient,
        IEnumerable<AIContextProvider> contextProviders,
        IReadOnlyList<AITool> tools)
    {
        _innerAgent = chatClient
            .AsAIAgent(
                name: "NanoBot",
                instructions: BuildInstructionsAsync,
                tools: tools,
                contextProviders: contextProviders.ToList())
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();
    }
    
    public async Task<AgentRunResponse> RunAsync(string input, AgentThread? thread = null, CancellationToken ct = default)
    {
        return await _innerAgent.RunAsync(input, thread, ct);
    }
    
    public async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        string input, 
        AgentThread? thread = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var update in _innerAgent.RunStreamingAsync(input, thread, ct))
        {
            yield return update;
        }
    }
}
```

---

#### Task 3.3.2: 实现指令构建逻辑

**描述**: 实现动态构建 Agent 指令的逻辑。

**交付物**:
- `BuildInstructionsAsync` 方法实现
- 加载 AGENTS.md、SOUL.md、Skills

**完成标准**:
- 正确构建系统指令
- 支持动态更新

---

#### Task 3.3.3: 实现中间件管道

**描述**: 使用 `AIAgentBuilder` 添加中间件。

**交付物**:
- 日志中间件
- 记忆更新中间件
- 速率限制中间件

**完成标准**:
- 中间件正确执行
- 支持自定义扩展

---

#### Task 3.3.4: 编写 NanoBotAgent 测试

**描述**: 编写 NanoBotAgent 的单元测试。

**交付物**:
- `NanoBot.Agent.Tests/NanoBotAgentTests.cs` 文件

**完成标准**:
- 测试覆盖率 >= 80%
- 所有测试通过

### 成功指标

- NanoBotAgent 正确封装 ChatClientAgent
- 支持同步和流式响应
- 中间件正确执行

---

## 任务清单：Agent 运行时模块

### 任务目标

实现 Agent 运行时，处理消息总线的消息。

### 相关方案文档

- [Agent-Core.md](../solutions/Agent-Core.md) - Agent 运行时

### 任务依赖

- NanoBotAgent 模块
- 消息总线
- 通道管理器

### 任务列表

#### Task 3.4.1: 实现 AgentRuntime 类

**描述**: 实现 Agent 运行时，监听消息总线。

**交付物**:
- `NanoBot.Agent/AgentRuntime.cs` 文件
- 订阅入站消息
- 调用 NanoBotAgent

**完成标准**:
- 正确监听消息总线
- 正确调用 Agent

---

#### Task 3.4.2: 实现会话管理

**描述**: 使用框架的 `AgentThread` 管理会话。

**交付物**:
- 会话创建逻辑
- 会话缓存逻辑

**完成标准**:
- 正确创建和获取会话
- 支持会话持久化

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

#### Task 3.4.4: 实现子 Agent 管理

**描述**: 实现 spawn 工具的子 Agent 创建。

**交付物**:
- 子 Agent 创建逻辑
- 任务执行和结果收集

**完成标准**:
- 正确创建子 Agent
- 正确返回结果

---

#### Task 3.4.5: 编写运行时测试

**描述**: 编写运行时的单元测试。

**交付物**:
- `NanoBot.Agent.Tests/AgentRuntimeTests.cs` 文件

**完成标准**:
- 测试覆盖率 >= 80%
- 所有测试通过

### 成功指标

- 消息处理流程正确
- 会话管理正确
- 子 Agent 创建正确

---

## 项目目录结构

```
src/
├── NanoBot.Core/
│   └── Memory/
│       └── IMemoryStore.cs
│
├── NanoBot.Agent/
│   ├── NanoBotAgent.cs
│   ├── AgentRuntime.cs
│   └── Context/
│       ├── BootstrapContextProvider.cs
│       ├── MemoryContextProvider.cs
│       ├── HistoryContextProvider.cs
│       └── SkillsContextProvider.cs
│
├── NanoBot.Infrastructure/
│   └── Memory/
│       └── MemoryStore.cs
│
└── tests/
    ├── NanoBot.Agent.Tests/
    │   ├── NanoBotAgentTests.cs
    │   ├── AgentRuntimeTests.cs
    │   └── Context/
    │       └── ContextProviderTests.cs
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
| 会话持久化问题 | 中 | 低 | 实现 FileBackedAgentThread |

---

## 阶段完成标准

- [ ] 所有 ContextProvider 实现完成
- [ ] MemoryStore 实现完成
- [ ] NanoBotAgent 封装完成
- [ ] AgentRuntime 实现完成
- [ ] 所有单元测试通过
- [ ] Agent 循环可正常运行

## 下一阶段

完成本阶段后，进入 [Phase 4: 应用层](./Phase4-Application.md)。
