# NanoBot.Net 功能对齐方案（精简版）- Nanobot 最近一周更新

**日期**: 2025-03-03  
**版本**: v0.1.4.post2  
**更新范围**: 2025年2月24日 - 2025年3月3日

## 概述

根据 nanobot 最近一周的 Git 更新分析，本次更新主要包含以下核心功能增强：

1. **可扩展命令系统与任务取消机制**
2. **运行时上下文优化与缓存稳定性**
3. **心跳服务重构**
4. **通道功能增强**
5. **内存与工具优化**

## 主要功能更新分析

以下内容已根据 Microsoft.Agents.AI 和 NanoBot.Net 现有结构重新梳理，强调“增量改造 + 复用原生能力”。

### 1. 可扩展命令系统 (PR #1180)

**现状**:
- `AgentRuntime.ProcessMessageAsync` 已能处理 `/new`、`/help`，只是硬编码。
- Microsoft.Agents.AI 的 `ChatClientAgent` 支持自定义前后处理 hook，无需额外调度层。

**优化方向**:
- 保持命令逻辑位于 `AgentRuntime`，改为维护一个轻量 `Dictionary<string, CommandHandler>`。
- 立即命令（如 `/stop`）在 `RunAsync` 拉取消息后优先处理；非立即命令继续走 `_agent.RunAsync`。
- 命令帮助文本直接由该字典生成，无需额外接口层。

**实现要点**:
1. 新增内部结构 `record CommandDefinition(string Name, string Description, bool Immediate, Func<InboundMessage, Task<OutboundMessage?>> Handler)`。
2. 在 `AgentRuntime` 构造函数中注册 `/new`、`/help`、`/stop`。
3. `/stop` handler 将触发任务/子代理取消（见下一节）。

### 2. 任务取消与子代理管理 (PR #1180)

**现状**:
- `SubagentManager` 已定义 `Cancel(string id)` 并追踪活动子代理。
- `SessionManager` 能够基于 `sessionKey` 管理上下文。
- `AgentRuntime` 内部尚未对“当前会话任务”进行显式跟踪。

**优化方向**:
- 依托现有 `SessionManager` + `SubagentManager`，在 Agent 层维持“会话 -> CancellationTokenSource”映射，而非在 Core 中新增 TaskManager。
- `/stop` 命令触发：
  1. 取消当前会话的 `CancellationTokenSource`；
  2. 调用 `SubagentManager.Cancel(id)`/`Cancel(sessionId)`（需要在基础实现中添加 session 维度但仍位于 Infrastructure 层）。
- 通过 Microsoft.Agents.AI 的 Streaming API，可在 tool 调用阶段注入取消令牌。

**实现要点**:
1. 在 `AgentRuntime` 中新增 `_sessionCts = ConcurrentDictionary<string, CancellationTokenSource>`。
2. `ProcessMessageAsync` 获取/创建 CTS，并传给 `_agent.RunAsync`。
3. `/stop` handler 调用 `TryCancelSession(sessionKey)` 并遍历 `SubagentManager` 内记录的该会话子代理执行取消。

### 3. 运行时上下文层优化 (PR #1126)

**现状**:
- .NET 版已将上下文拆分为多个 `ContextProvider`，可以在 BuildPrompt 阶段插入信息。
- Microsoft.Agents.AI `AgentSession` 支持 Metadata，可用于携带“不可信”信息。

**优化方向**:
- 不新增新的 Core 接口，直接在 `BootstrapContextProvider` 和 `CompositeChatHistoryProvider` 内的内存拼装流程加入“Untrusted Runtime Context”段落。
- 通过 `AgentSession.SetState("runtime:untrusted", metadata)` 存储运行时信息，并在 Provider 构建 system prompt 时以固定标题输出。
- 为了提升缓存稳定性，确保该段落在 prompt 中位置固定且包含“仅供参考”文案。

**实现要点**:
1. 在 `AgentRuntime` 或 `SessionManager` 中暴露 `SetRuntimeMetadata(sessionKey, IReadOnlyDictionary<string,string>)`。
2. `BootstrapContextProvider` 读取 metadata，构建 `Untrusted runtime context` 小节。
3. 保持原有 prompt 结构，避免变更 Providers 的公共签名。

### 4. 心跳服务重构 (PR #1102)

**现状**:
- `NanoBot.Core.Heartbeat.IHeartbeatService` 采用 Job 模型，可在 CLI/服务中调度。
- 需要将 nanobot 的两阶段逻辑内联到现有 HeartbeatService，而不是重写接口。

**优化方向**:
- 保持 `Start/Stop/AddJob` 等 API，内部在执行 Job 时新增：
  1. Phase 1：使用 ChatClientAgent + 虚拟工具定义发起决策。
  2. Phase 2：当决策为 `run` 时，通过 `AgentRuntime.ProcessSystemMessageAsync` 执行任务。
- 虚拟工具定义可放在 HeartbeatService 内部静态字段，直接用 Microsoft.Agents.AI 的 `FunctionToolDefinition`。

**实现要点**:
1. 在 HeartbeatService 中注册 `FunctionDefinition HeartbeatTool`（不对外暴露）。
2. Phase 1 调用 `_agent.RunAsync`，监听 `FunctionCallContent` 返回 `action` 与 `tasks`。
3. Phase 2 复用现有 `TriggerNowAsync` 流程，只是通过新的决策结果决定是否执行。
4. CLI 显示的心跳间隔日志保持兼容。

### 5. 飞书通道图像支持 (PR #1090)

### 5. 飞书通道图像支持 (PR #1090)

**更新内容**:
- 飞书通道支持富文本消息中的图像
- 图像提取和下载功能
- 帖子消息图像处理

**.NET 实现要点**:
- 在 `FeishuChannel` 现有 `MsgTypeMap` 基础上解析 `post` 消息；
- 新增私有 helper（类内方法即可）提取 `image_key` 并用现有 HttpClient 下载；
- 使用 `_mediaDirectory` 缓存图像并在发往总线时写入 `[image: path]` 占位；
- 维持当前 `ChannelBase` 结构，不增加新的公共类型。

### 6. 内存与工具优化

**实现要点**:
- 在 `MemoryConsolidator` 和 `MemoryStore` 内部新增 base64 过滤逻辑即可。
- `SessionManager.SaveSessionAsync` 增强对无 tool call 回复的持久化，不必扩展 Interface。
- `WebFetchTool` 直接延用 Microsoft.Extensions.AI 的配置模式，从 `IConfiguration` 读取 API Key，以避免重复接口。

## 实现优先级

### 高优先级 (P0)
1. **命令系统增量扩展** - 精准定位 `AgentRuntime`
2. **任务取消与子代理协调** - 利用现有 Session/Token 机制
3. **运行时上下文优化** - Provider 内部调整

### 中优先级 (P1)
1. **心跳服务两阶段** - 内部逻辑升级
2. **内存与工具改进** - 针对性修复
3. **通道增强** - 平台特性补齐

### 低优先级 (P2)
1. **通道功能增强** - 特定平台功能
2. **文档更新** - 配置和文档同步

## 技术债务清理

基于 nanobot 的重构更新，建议同步清理以下技术债务：

1. **helpers.py 精简** - 移除死代码，压缩文档字符串
2. **工具注册简化** - 移除冗余代码
3. **消息处理优化** - 简化内联整合锁
4. **测试覆盖** - 新增任务取消、上下文缓存等测试

## 兼容性考虑

1. **配置兼容性** - 新增命令系统配置项
2. **API 兼容性** - 保持现有工具接口不变
3. **会话兼容性** - 确保现有会话数据迁移
4. **通道兼容性** - 新增功能向后兼容

## 下一步行动

1. 创建详细的实现任务卡片
2. 更新项目架构文档
3. 制定测试策略
4. 确定发布里程碑

## 相关文档

- [nanobot 原项目代码](../../../Temp/nanobot/)
- [Microsoft.Agents.AI 文档](../../../Temp/agent-framework/)
- [项目架构设计](../design/)
- [更新历史](./archive/)
