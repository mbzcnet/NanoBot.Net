# 记忆机制对比报告

## 执行日期
2026-02-22

---

## 一、概述

本报告对比 Python 原项目（nanobot）和 .NET 移植版本（NanoBot.Net）中记忆（Memory）机制的实现情况。

---

## 二、原项目（nanobot）实现

### 2.1 核心组件：MemoryStore

**文件位置**: `Temp/nanobot/nanobot/agent/memory.py`

```python
class MemoryStore:
    """Two-layer memory: MEMORY.md (long-term facts) + HISTORY.md (grep-searchable log)."""

    def __init__(self, workspace: Path):
        self.memory_dir = ensure_dir(workspace / "memory")
        self.memory_file = self.memory_dir / "MEMORY.md"
        self.history_file = self.memory_dir / "HISTORY.md"
```

**核心方法**:
| 方法 | 功能 |
|------|------|
| `read_long_term()` | 读取 MEMORY.md 文件内容 |
| `write_long_term(content)` | 写入 MEMORY.md 长期记忆 |
| `append_history(entry)` | 追加历史记录到 HISTORY.md |
| `get_memory_context()` | 返回格式化的记忆上下文 |

### 2.2 记忆使用方式

**文件位置**: `Temp/nanobot/nanobot/agent/context.py`

在 `ContextBuilder` 中：
1. 创建 `MemoryStore` 实例
2. 在 `build_system_prompt()` 中调用 `get_memory_context()`
3. 将记忆内容附加到系统提示词中

```python
# Memory context
memory = self.memory.get_memory_context()
if memory:
    parts.append(f"# Memory\n\n{memory}")
```

### 2.3 记忆整合（Consolidation）

**文件位置**: `Temp/nanobot/nanobot/agent/loop.py`

在 `AgentLoop` 中通过 `_consolidate_memory()` 方法实现：

```python
async def _consolidate_memory(self, session, archive_all: bool = False) -> None:
    # 1. 计算需要整合的消息
    # 2. 调用 LLM 生成摘要
    # 3. 更新 MEMORY.md 和 HISTORY.md
    # 4. 更新 last_consolidated 索引
```

**触发时机**:
- 定期触发：当消息数超过阈值时
- 会话结束时：`archive_all=True`

---

## 三、移植版本（NanoBot.Net）实现

### 3.1 接口定义：IMemoryStore

**文件位置**: `src/NanoBot.Core/Memory/IMemoryStore.cs`

```csharp
public interface IMemoryStore
{
    Task<string> LoadAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(
        IEnumerable<ChatMessage> requestMessages,
        IEnumerable<ChatMessage> responseMessages,
        CancellationToken cancellationToken = default);

    Task AppendHistoryAsync(string entry, CancellationToken cancellationToken = default);

    string GetMemoryContext();
}
```

### 3.2 实现：MemoryStore

**文件位置**: `src/NanoBot.Infrastructure/Memory/MemoryStore.cs`

**特点**:
- 使用 `SemaphoreSlim` 实现线程安全
- 内存缓存 `_cachedMemory`
- 支持配置启用/禁用

**核心方法对比**:

| Python (nanobot) | C# (NanoBot.Net) |
|------------------|------------------|
| `read_long_term()` | `LoadAsync()` |
| `write_long_term()` | `UpdateAsync()` |
| `append_history()` | `AppendHistoryAsync()` |
| `get_memory_context()` | `GetMemoryContext()` |

### 3.3 记忆整合器：MemoryConsolidator

**文件位置**: `src/NanoBot.Infrastructure/Memory/MemoryConsolidator.cs`

实现 LLM 驱动的记忆整合：
- 接收消息列表和已整合索引
- 调用 LLM 生成摘要
- 返回 JSON 格式的 `history_entry` 和 `memory_update`

### 3.4 上下文提供者

**MemoryContextProvider** (`src/NanoBot.Agent/Context/MemoryContextProvider.cs`):
- 继承 `AIContextProvider`（Microsoft.Agents.AI 框架）
- `InvokingCoreAsync`: 加载记忆并注入到 AI 上下文
- `InvokedCoreAsync`: 更新记忆

**MemoryConsolidationContextProvider** (`src/NanoBot.Agent/Context/MemoryConsolidationContextProvider.cs`):
- ⚠️ **注意**: 当前为空实现，只有 `InvokingCoreAsync` 返回空上下文
- 记忆整合功能**未被实际调用**

---

## 四、差异分析

### 4.1 架构差异

| 方面 | nanobot (Python) | NanoBot.Net (C#) |
|------|------------------|------------------|
| 框架 | 直接实现 | 基于 Microsoft.Agents.AI |
| 上下文注入 | ContextBuilder 构建 | AIContextProvider |
| 文件操作 | 同步读写 | 异步 + 缓存 |
| 线程安全 | 无 | SemaphoreSlim |

### 4.2 功能完整性

| 功能 | nanobot | NanoBot.Net | 状态 |
|------|---------|-------------|------|
| 长期记忆 (MEMORY.md) | ✅ | ✅ | 已实现 |
| 历史记录 (HISTORY.md) | ✅ | ✅ | 已实现 |
| 记忆上下文注入 | ✅ | ✅ | 已实现 |
| 记忆更新 | ✅ | ✅ | 已实现 |
| 记忆整合 (LLM) | ✅ | ⚠️ | 未集成 |

### 4.3 关键问题

**问题 1：记忆整合未集成**
- `MemoryConsolidator` 已实现但未被调用
- `MemoryConsolidationContextProvider` 是空实现
- 需要在 Agent 运行时集成记忆整合逻辑

**问题 2：架构差异**
- Python 版本使用 ContextBuilder 直接构建提示词
- C# 版本使用 Microsoft.Agents.AI 的 AIContextProvider
- 需要确保两种架构下记忆行为一致

---

## 五、结论

### 5.1 已完成移植
- ✅ 核心记忆存储（MEMORY.md + HISTORY.md）
- ✅ 记忆上下文加载与注入
- ✅ 记忆更新机制

### 5.2 待完成
- 🔄 记忆整合（Consolidation）功能的实际调用
- 🔄 与 Microsoft.Agents.AI 框架的深度集成

### 5.3 建议
1. 在 AgentRuntime 中添加记忆整合触发逻辑
2. 考虑将 MemoryConsolidationContextProvider 完善为真正的整合提供者
3. 参考 Python 版本在会话结束时触发整合

---

## 附录：相关文件索引

### 原项目 (Python)
- `Temp/nanobot/nanobot/agent/memory.py` - MemoryStore 实现
- `Temp/nanobot/nanobot/agent/context.py` - ContextBuilder 使用记忆
- `Temp/nanobot/nanobot/agent/loop.py` - AgentLoop 中的整合逻辑

### 移植版本 (C#)
- `src/NanoBot.Core/Memory/IMemoryStore.cs` - 接口定义
- `src/NanoBot.Infrastructure/Memory/MemoryStore.cs` - 实现
- `src/NanoBot.Infrastructure/Memory/MemoryConsolidator.cs` - 整合器
- `src/NanoBot.Agent/Context/MemoryContextProvider.cs` - 上下文提供者
- `src/NanoBot.Agent/Context/MemoryConsolidationContextProvider.cs` - 整合提供者（未完成）
