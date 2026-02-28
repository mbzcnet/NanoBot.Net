# Workspace 对齐原项目的优化/修改方案

**目标**：使 NanoBot.Net 的 Workspace/Bootstrap/Heartbeat/Memory/Session 行为与 nanobot 原项目（见报告 `doc.ai/reports/update/workspace-comparison.md`）保持一致；避免重复造轮子，优先复用现有基础设施与 Microsoft.Agents.AI 能力。

---

## 0. 范围与非目标

### 范围
- Workspace 模板资源的组织与提取（`templates/`）
- 引导文件（bootstrap files）内容对齐：`AGENTS.md`、`TOOLS.md`（COPY 原项目版本）
- Heartbeat 机制对齐：从 “HEARTBEAT_OK token + free-text” 改为 “两阶段（decision/execution）+ 虚拟工具调用”
- Memory/History 对齐：双层文件、追加式 HISTORY、LLM 工具调用式 consolidate（`save_memory`）
- Sessions 对齐：JSONL、append-only、`last_consolidated`、legacy 迁移路径
- CLI history 的语义区分（不与 memory/HISTORY 混淆）

### 非目标
- 不在本方案中落地具体实现代码（仅设计、接口与数据结构）
- 不扩展新功能（如新增 channels/providers），仅做“与原项目一致”的改造

---

## 1. 现状与差距（摘要）

### 1.1 模板目录命名
- **原项目**：`nanobot/templates/`
- **当前**：`src/workspace/`，并且嵌入式资源前缀硬编码为 `workspace/`

### 1.2 Bootstrap 引导文件内容
- **差距点**：当前 `AGENTS.md`、`TOOLS.md` 过度详细，与原项目精简风格不一致。
- **对齐要求**：直接 COPY 原项目版本。

### 1.3 Heartbeat 机制
- **原项目**：两阶段（决策/执行），决策通过虚拟工具 `heartbeat(action=skip|run, tasks=...)` 返回结构化结果。
- **当前**：通过提示词要求 LLM 返回 `HEARTBEAT_OK` token，属于 free-text 解析，鲁棒性较差。

### 1.4 Memory/History 机制
- **原项目**：MemoryStore consolidate 通过虚拟工具 `save_memory(history_entry, memory_update)` 来更新 `MEMORY.md` 与追加 `HISTORY.md`。
- **当前**：MemoryStore 以“Recent Conversations”拼接方式写入 `MEMORY.md`，未实现结构化 consolidate，也未对齐 `history_entry` 时间戳格式。

### 1.5 Sessions 机制
- **原项目**：workspace 下 `sessions/*.jsonl`，首行 metadata，消息 append-only；含 `last_consolidated` 且支持 legacy `~/.nanobot/sessions/` 迁移。
- **当前**：使用 Agents 框架序列化为单个 `.json` 文件；不满足 JSONL/append-only/迁移/`last_consolidated` 等一致性要求。

---

## 2. 对齐目标（可验收标准）

### 2.1 模板资源
- 资源目录命名对齐为 `templates/`。
- 可从嵌入式资源中提取如下文件到用户 workspace：
  - `AGENTS.md`、`SOUL.md`、`TOOLS.md`、`USER.md`、`HEARTBEAT.md`
  - `memory/MEMORY.md`（模板）
- 提取规则：若目标文件已存在，则跳过（与当前 `WorkspaceManager` 行为一致）。

### 2.2 Heartbeat
- Heartbeat 的“是否执行”决策必须是结构化结果，不依赖 token/自由文本。
- 决策阶段输入为 `HEARTBEAT.md` 文件内容；输出为：
  - `action: skip|run`
  - `tasks: string`（仅 run 时必填）

### 2.3 Memory/History
- `memory/MEMORY.md`：长期事实（完整覆盖更新）。
- `memory/HISTORY.md`：追加式事件日志，每次追加一个条目，条目以 `[YYYY-MM-DD HH:MM]` 或同等可 grep 的时间戳开头（与原项目一致）。
- consolidate 由 LLM 工具调用返回结构化字段：
  - `history_entry`
  - `memory_update`

### 2.4 Sessions
- 会话文件为 JSONL：
  - 首行 metadata：`_type=metadata`、`key`、`created_at`、`updated_at`、`last_consolidated`、`metadata`。
  - 后续行：消息对象（append-only）。
- `GetHistory(max_messages)` 只返回未 consolidate 的消息片段，并对齐到用户轮次（避免 tool_result 孤儿块）。
- legacy 迁移：若存在 `~/.nanobot/sessions/{safe_key}.jsonl`，首次读取时迁移到 `workspace/sessions/`。

---

## 3. 设计方案

### 3.1 模板资源目录与加载

#### 3.1.1 目录与资源前缀
- 将 `src/workspace/` 迁移为 `src/templates/`（仅资源组织；用户 workspace 目录仍由 `WorkspaceConfig.Path` 决定）。
- 嵌入式资源前缀从 `workspace/` 调整为 `templates/`。

#### 3.1.2 资源加载接口（保持现有抽象）
- 保持 `IEmbeddedResourceLoader` 不变或最小扩展。
- 调整 `EmbeddedResourceLoader.GetWorkspaceResourceNames()` 的匹配逻辑：
  - 由 `StartsWith("workspace/")` 改为 `StartsWith("templates/")`。
- 调整 `WorkspaceManager.ExtractDefaultFilesFromResourcesAsync()` 中的前缀参数：
  - `ConvertResourceNameToRelativePath(resourceName, "workspace")` -> `(..., "templates")`

#### 3.1.3 Bootstrap 文件集合
- `BootstrapLoader.BootstrapFiles` 与原项目对齐（仅 4 个）：
  - `AGENTS.md`、`SOUL.md`、`USER.md`、`TOOLS.md`
- `HEARTBEAT.md` 不属于 system prompt 的 bootstrap（原项目也是 heartbeat service 单独读取），但仍作为模板文件存在于 workspace 根目录。

---

### 3.2 引导文件内容对齐策略

#### 3.2.1 COPY 策略
- 以原项目为权威来源：
  - `templates/AGENTS.md` 直接复制原项目 `nanobot/templates/AGENTS.md`
  - `templates/TOOLS.md` 直接复制原项目 `nanobot/templates/TOOLS.md`
- 其余文件若一致（`SOUL.md`、`USER.md`、`HEARTBEAT.md`）可保持不变，但建议也按 COPY 统一来源以避免漂移。

#### 3.2.2 memory 模板
- 添加 `templates/memory/MEMORY.md`（COPY 原项目）。
- workspace 初始化时若 `memory/MEMORY.md` 不存在，则写入模板内容。

---

### 3.3 Heartbeat：两阶段 + 虚拟工具调用

#### 3.3.1 新的决策输出模型
在 `NanoBot.Core` 定义纯数据模型（示意）：

```csharp
namespace NanoBot.Core.Heartbeat;

public enum HeartbeatDecisionAction
{
    Skip,
    Run
}

public sealed record HeartbeatDecision(
    HeartbeatDecisionAction Action,
    string? Tasks
);
```

#### 3.3.2 服务接口建议
保持 `IHeartbeatService` 现有方法，但内部实现改为两阶段。

新增一个内部可测试的决策方法（示意签名）：

```csharp
internal interface IHeartbeatDecider
{
    Task<HeartbeatDecision> DecideAsync(string heartbeatMarkdown, CancellationToken cancellationToken);
}
```

#### 3.3.3 与 AgentRuntime 的衔接
- Phase 1：decider 仅调用 LLM（或 Agent 的 tool calling）生成 `HeartbeatDecision`。
- Phase 2：若 `Run`，通过已有的 `onHeartbeat` 回调（或替换为 `Func<string, Task<string>> onExecute`）执行完整 agent loop。

> 说明：当前 `HeartbeatService` 通过 `_onHeartbeat(prompt)` 让上层执行一次 agent；建议改为：
> - 决策 prompt 输入为 `HEARTBEAT.md` 内容
> - 执行 prompt 输入为 `tasks`（由决策阶段生成的自然语言任务摘要）

---

### 3.4 Memory/History：双层文件 + consolidate 工具调用

#### 3.4.1 文件职责
- `memory/MEMORY.md`：长期事实集合（每次 consolidate 输出全量覆盖更新）。
- `memory/HISTORY.md`：事件日志（追加）。

#### 3.4.2 consolidate 工具调用契约
在 Memory 模块定义工具调用结果模型（示意）：

```csharp
namespace NanoBot.Core.Memory;

public sealed record MemoryConsolidationResult(
    string HistoryEntry,
    string MemoryUpdate
);
```

以及 consolidate 执行器接口（示意）：

```csharp
namespace NanoBot.Core.Memory;

public interface IMemoryConsolidator
{
    Task<MemoryConsolidationResult?> ConsolidateAsync(
        IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> conversationToProcess,
        string currentLongTermMemory,
        CancellationToken cancellationToken = default);
}
```

#### 3.4.3 与 Session 的耦合点
- 原项目依赖 `session.last_consolidated` 与 “保留窗口”策略。
- .NET 侧需要在 Session 数据中保存 `LastConsolidated`（见 3.5 JSONL 方案），MemoryConsolidator 根据该字段决定 consolidate 哪段消息。

#### 3.4.4 HISTORY 追加格式
- 与原项目一致：每个条目独立段落、时间戳在行首。
- 当前实现使用 UTC 秒级时间戳；建议对齐到分钟粒度（或至少保持可 grep 的 `[yyyy-MM-dd HH:mm]` 前缀）。

---

### 3.5 Sessions：JSONL + append-only + legacy 迁移

#### 3.5.1 Session 存储模型
定义与原项目一致的 JSONL schema（概念层）：

- metadata line:
  - `_type = "metadata"`
  - `key: string`
  - `created_at: string (iso)`
  - `updated_at: string (iso)`
  - `last_consolidated: int`
  - `metadata: object`

- message line:
  - `role: string`
  - `content: string`
  - `timestamp: string (iso)`
  - `tool_calls/tool_call_id/name` 等（按现有消息结构需要）

#### 3.5.2 与 Microsoft.Agents.AI 的关系
当前 `SessionManager` 使用 `ChatClientAgent.SerializeSessionAsync()` 持久化完整 session JSON。

为保持与原项目一致，建议：
- **引入 NanoBot 自有的 session 持久化层**（JSONL），并在运行时构造用于 Agents 的输入消息列表。
- Agents 框架的 `AgentSession` 可继续用于一次运行期的上下文，但落盘以 JSONL 为准。

#### 3.5.3 legacy 迁移策略
- legacy 路径：`~/.nanobot/sessions/`
- 迁移触发：读取 session 时若 workspace 目标不存在但 legacy 存在，则移动到 workspace。

---

## 4. 迁移与兼容性策略

### 4.1 模板目录变更的兼容
- 嵌入式资源前缀变更后，不影响用户现有 workspace 目录内容。
- 若用户已经初始化过 workspace，不会覆盖现有文件（保持“存在则跳过”的策略）。

### 4.2 Sessions 落盘格式迁移
- 当前已有 `.json` 会话文件：
  - 提供一次性迁移：首次读取时若 `.jsonl` 不存在但 `.json` 存在，则尝试转换为 JSONL。
  - 转换失败则保留 `.json`，并创建新的 `.jsonl`（仅从后续消息开始 append），同时记录日志。

> 是否要做 `.json` -> `.jsonl` 的完整迁移取决于 Agents session JSON 的可解析程度；建议先实现“向前兼容读取”，再逐步迁移。

---

## 5. 实施步骤（建议 PR 切分）

### PR-1：模板资源对齐
- 目录：`src/workspace` -> `src/templates`
- 更新 `EmbeddedResourceLoader` 与 `WorkspaceManager` 的资源前缀
- COPY 原项目 `AGENTS.md`、`TOOLS.md`
- 添加 `templates/memory/MEMORY.md`
- 更新 `NanoBot.Infrastructure.csproj`（若存在显式嵌入资源配置）以确保新路径被嵌入

### PR-2：Heartbeat 两阶段
- 引入 `HeartbeatDecision` 模型
- 重写 `HeartbeatService`：决策阶段返回结构化 decision；执行阶段调用现有 agent loop
- 为 Heartbeat 决策添加单元测试（mock LLM/tool call 结果）

### PR-3：Memory consolidate 对齐
- 引入 `IMemoryConsolidator` 与 `MemoryConsolidationResult`
- 调整 `MemoryStore.UpdateAsync` 从“拼接 recent conversations”改为“调用 consolidator”
- 追加 HISTORY 格式对齐

### PR-4：Sessions JSONL
- 新增 JSONL Session 存储实现与接口
- 加入 legacy 迁移逻辑
- 与 Memory 的 `last_consolidated` 对齐

---

## 6. 验证与测试清单（验收点）

- Workspace 初始化后生成文件集与原项目一致：
  - 根目录包含 `AGENTS/SOUL/TOOLS/USER/HEARTBEAT`
  - `memory/MEMORY.md` 存在且具备模板结构
  - `memory/HISTORY.md` 存在（可为空）
  - `sessions/` 存在
  - `skills/` 存在

- Heartbeat：
  - `HEARTBEAT.md` 空/仅注释时不会触发执行
  - `HEARTBEAT.md` 有任务时，会先产生结构化 decision，再执行任务

- Memory：
  - consolidate 后 `MEMORY.md` 被全量更新
  - `HISTORY.md` 追加新条目，条目可 grep，时间戳格式一致

- Sessions：
  - 新会话落盘为 `.jsonl`
  - 写入是 append-only（不重写历史消息行）
  - `last_consolidated` 正确更新
  - legacy 目录存在时可自动迁移

---

## 7. 待确认问题（需要你拍板）

1. **是否必须 100% 复刻原项目 sessions JSONL 结构**，还是允许保留 Agents 的 `.json` 同时新增 `.jsonl`（双写）过渡？
2. **Heartbeat 的 LLM 调用入口**：
   - 继续用当前的 `Func<string, Task<string>> onHeartbeat`（上层负责调用 agent）
   - 还是让 HeartbeatService 直接持有 agent/LLM client 来完成 Phase 1 的 tool-calling？
3. **时间戳标准**：是否严格对齐原项目的 `[YYYY-MM-DD HH:MM]`（本地时区）还是统一 UTC？

