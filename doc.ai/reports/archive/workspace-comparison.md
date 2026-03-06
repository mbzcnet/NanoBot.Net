# Workspace 方案对比报告

**日期**: 2026-02-27  
**对比对象**: nanobot (Python) vs NanoBot.Net (C#)

---

## 一、目录结构对比

### 原项目 (nanobot)
```
nanobot/templates/
├── AGENTS.md
├── SOUL.md
├── TOOLS.md
├── USER.md
├── HEARTBEAT.md
└── memory/
    └── MEMORY.md
```

### 当前项目 (NanoBot.Net)
```
src/workspace/
├── AGENTS.md
├── SOUL.md
├── TOOLS.md
├── USER.md
├── HEARTBEAT.md
└── memory/
    └── (空目录)
```

**差异**:
- 原项目使用 `templates/` 目录名，当前项目使用 `workspace/`
- 原项目在 `templates/memory/` 下有 `MEMORY.md` 模板文件
- 当前项目的 `memory/` 目录为空

---

## 二、引导文件内容对比

### 1. AGENTS.md

#### 原项目版本
- **Guidelines**: 更精确的工具调用指导
  - "Before calling tools, briefly state your intent — but NEVER predict results before receiving them"
  - "Use precise tense: 'I will run X' before the call, 'X returned Y' after"
  - "NEVER claim success before a tool result confirms it"
- **简洁**: 32 行，更紧凑
- **工具列表**: 不在 AGENTS.md 中列出（在 TOOLS.md 中）

#### 当前项目版本
- **Guidelines**: 更通用的指导
  - "Always explain what you're doing before taking actions"
  - "Ask for clarification when the request is ambiguous"
- **详细**: 52 行，包含更多示例
- **工具列表**: 在 AGENTS.md 中列出了所有可用工具
- **额外内容**: 包含详细的 Heartbeat 任务格式示例

**需要更新**: ✅ 应采用原项目的精简版本

---

### 2. SOUL.md

#### 对比结果
- **完全一致**: 两个版本内容相同（22 行）
- 包含 Personality, Values, Communication Style

**需要更新**: ❌ 无需修改

---

### 3. TOOLS.md

#### 原项目版本
- **简洁**: 16 行
- **内容**: 只记录非显而易见的约束和使用模式
- **说明**: "Tool signatures are provided automatically via function calling"
- **重点**: exec 的安全限制，cron 技能引用

#### 当前项目版本
- **详细**: 151 行
- **内容**: 完整的工具签名文档
- **包含**: 所有工具的详细说明、参数、示例代码

**需要更新**: ✅ 应采用原项目的精简版本（工具签名由 function calling 自动提供）

---

### 4. USER.md

#### 对比结果
- **完全一致**: 两个版本内容相同（50 行）
- 包含用户信息、偏好设置、工作上下文等模板

**需要更新**: ❌ 无需修改

---

### 5. HEARTBEAT.md

#### 对比结果
- **完全一致**: 两个版本内容相同（17 行）
- 包含周期性任务管理说明

**需要更新**: ❌ 无需修改

---

## 三、HEARTBEAT 实现机制

### 原项目实现

**文件位置**: `nanobot/heartbeat/service.py`

**核心机制**:
1. **HeartbeatService** 类负责周期性检查
2. **两阶段执行**:
   - **Phase 1 (决策)**: 读取 `HEARTBEAT.md`，通过虚拟工具调用让 LLM 判断是否有任务
   - **Phase 2 (执行)**: 仅在 Phase 1 返回 `run` 时触发，执行完整的 agent loop
3. **虚拟工具**: `heartbeat` 工具，返回 `action` (skip/run) 和 `tasks` 摘要
4. **配置参数**:
   - `interval_s`: 默认 1800 秒（30 分钟）
   - `enabled`: 可开关
   - `on_execute`: 执行回调
   - `on_notify`: 通知回调

**文件路径**: `workspace/HEARTBEAT.md`（直接在 workspace 根目录）

### 当前项目实现

**文件位置**: `src/NanoBot.Infrastructure/Heartbeat/HeartbeatService.cs`

**需要检查**: 是否实现了相同的两阶段机制和虚拟工具调用

---

## 四、Memory 和 History 实现

### 原项目实现

**MemoryStore** (`nanobot/agent/memory.py`):

1. **双层记忆系统**:
   - `memory/MEMORY.md`: 长期事实（用户偏好、上下文、关系）
   - `memory/HISTORY.md`: 仅追加的事件日志（可用 grep 搜索）

2. **记忆整合** (consolidate):
   - 通过 LLM 工具调用 `save_memory` 进行整合
   - 参数:
     - `history_entry`: 2-5 句摘要，以 `[YYYY-MM-DD HH:MM]` 开头
     - `memory_update`: 完整的长期记忆 markdown
   - 支持 `archive_all` 模式和增量模式
   - 使用 `session.last_consolidated` 跟踪已整合的消息数

3. **文件路径**:
   - `workspace/memory/MEMORY.md`
   - `workspace/memory/HISTORY.md`

4. **模板文件**:
   - `templates/memory/MEMORY.md`: 提供初始结构

### 当前项目实现

**需要检查**: 
- `NanoBot.Infrastructure.Memory.MemoryStore` 是否实现了相同的双层系统
- 是否实现了 `save_memory` 虚拟工具调用
- 是否有 `memory/MEMORY.md` 模板文件

---

## 五、Sessions 实现

### 原项目实现

**SessionManager** (`nanobot/session/manager.py`):

1. **存储格式**: JSONL（每行一个 JSON 对象）
2. **存储位置**: 
   - 新版本: `workspace/sessions/{safe_key}.jsonl`
   - 旧版本（遗留）: `~/.nanobot/sessions/{safe_key}.jsonl`
   - 支持自动迁移
3. **Session 结构**:
   - `key`: channel:chat_id
   - `messages`: 消息列表（仅追加）
   - `created_at`, `updated_at`: 时间戳
   - `metadata`: 元数据字典
   - `last_consolidated`: 已整合的消息数
4. **JSONL 格式**:
   - 第一行: 元数据行（`_type: "metadata"`）
   - 后续行: 消息行
5. **get_history()**: 返回未整合的消息，对齐到用户轮次
6. **LLM 缓存优化**: 消息仅追加，整合过程不修改消息列表

### 当前项目实现

**需要检查**:
- `NanoBot.Agent.SessionManager` 是否使用 JSONL 格式
- 是否支持 `last_consolidated` 机制
- 是否有遗留路径迁移逻辑
- 存储位置是否在 `workspace/sessions/`

---

## 六、CLI History 实现

### 原项目实现

**位置**: `~/.nanobot/history/cli_history`

**用途**: 
- 存储 CLI 交互历史（使用 `prompt_toolkit.history.FileHistory`）
- 与 workspace 无关，是全局的 CLI 历史记录

**注意**: 这不是 `memory/HISTORY.md`，是两个不同的概念
- `cli_history`: 用户在 CLI 中输入的命令历史
- `memory/HISTORY.md`: Agent 的事件日志

---

## 七、需要更新的内容总结

### 1. 目录结构调整

**当前**: `src/workspace/`  
**建议**: 重命名为 `src/templates/`（与原项目一致）

### 2. 引导文件更新

#### AGENTS.md
- ✅ **需要更新**: 采用原项目的精简版本（32 行）
- 移除工具列表（由 function calling 自动提供）
- 更新 Guidelines 为更精确的工具调用指导

#### TOOLS.md
- ✅ **需要更新**: 采用原项目的精简版本（16 行）
- 只记录非显而易见的约束和使用模式
- 移除详细的工具签名文档

#### SOUL.md
- ❌ 无需更新（已一致）

#### USER.md
- ❌ 无需更新（已一致）

#### HEARTBEAT.md
- ❌ 无需更新（已一致）

### 3. 添加缺失的模板文件

- ✅ **需要添加**: `templates/memory/MEMORY.md`
  - 从原项目复制内容

### 4. 代码实现检查项

#### HeartbeatService
- 检查是否实现了两阶段机制（决策 + 执行）
- 检查是否使用虚拟工具调用 `heartbeat`
- 检查文件路径是否为 `workspace/HEARTBEAT.md`

#### MemoryStore
- 检查是否实现了双层记忆系统
- 检查是否使用虚拟工具调用 `save_memory`
- 检查是否支持 `last_consolidated` 机制
- 检查是否有 `memory/MEMORY.md` 模板文件

#### SessionManager
- 检查是否使用 JSONL 格式
- 检查是否支持 `last_consolidated` 机制
- 检查存储位置是否在 `workspace/sessions/`
- 检查是否有遗留路径迁移逻辑

---

## 八、实施建议

### 阶段 1: 目录和模板文件更新
1. 将 `src/workspace/` 重命名为 `src/templates/`
2. 更新 `AGENTS.md` 为原项目版本
3. 更新 `TOOLS.md` 为原项目版本
4. 添加 `templates/memory/MEMORY.md`

### 阶段 2: 代码实现验证
1. 检查 `HeartbeatService` 实现
2. 检查 `MemoryStore` 实现
3. 检查 `SessionManager` 实现
4. 更新相关的路径引用（workspace -> templates）

### 阶段 3: 测试验证
1. 测试 Heartbeat 周期性任务
2. 测试 Memory 整合功能
3. 测试 Session 持久化和恢复
4. 测试遗留路径迁移

---

## 九、关键发现

1. **目录命名**: 原项目使用 `templates/`，当前项目使用 `workspace/`，建议统一
2. **文件精简**: 原项目的引导文件更精简，避免重复（工具签名由 function calling 提供）
3. **虚拟工具**: 原项目使用虚拟工具调用（`heartbeat`, `save_memory`）来避免自由文本解析
4. **双层记忆**: `MEMORY.md` 存储事实，`HISTORY.md` 存储事件日志
5. **JSONL 格式**: Sessions 使用 JSONL 格式，支持增量读写和 LLM 缓存优化
6. **仅追加消息**: 消息列表仅追加，整合过程不修改历史消息（优化 LLM 缓存）
7. **遗留迁移**: 支持从 `~/.nanobot/sessions/` 迁移到 `workspace/sessions/`

---

**报告完成时间**: 2026-02-27 23:44 UTC+8
