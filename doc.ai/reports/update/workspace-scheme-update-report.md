# NanoBot.Net Workspace 方案更新报告

## 概述

基于对原 nanobot 项目（Python）的深入分析，需要将当前 NanoBot.Net 的 workspace 方案调整为与原项目保持一致。

## 主要差异分析

### 1. 目录结构差异

**原项目（Python）:**
```
nanobot/
├── templates/           # 模板文件目录
│   ├── AGENTS.md       # Agent 指令模板
│   ├── SOUL.md         # 灵魂/个性模板
│   ├── TOOLS.md        # 工具使用说明模板
│   ├── USER.md         # 用户信息模板
│   └── HEARTBEAT.md    # 心跳任务模板
├── agent/
│   ├── memory.py       # 记忆系统（MEMORY.md + HISTORY.md）
│   └── context.py      # 上下文构建（引用 templates/）
├── session/
│   └── manager.py      # 会话管理（sessions/ 目录）
└── heartbeat/
    └── service.py      # 心跳服务（处理 HEARTBEAT.md）
```

**当前项目（.NET）:**
```
src/
├── workspace/          # 嵌入式资源（需要改为 templates/）
│   ├── AGENTS.md      # 内容需要更新
│   ├── SOUL.md        # 内容一致
│   ├── TOOLS.md       # 内容差异大
│   ├── USER.md        # 内容一致
│   └── HEARTBEAT.md   # 存在但无服务实现
├── NanoBot.Core/
│   ├── Workspace/     # 工作区配置
│   └── Configuration/ # 缺乏 memory/session/heartbeat 服务
└── NanoBot.Agent/
    └── Context/       # 缺乏完整的上下文构建逻辑
```

### 2. Templates 内容对比

#### AGENTS.md 差异
**原项目内容特点：**
- 更详细的工具使用指导
- 包含 scheduled reminders 和 heartbeat tasks 说明
- 明确说明不要直接写提醒到 MEMORY.md

**当前项目内容：**
- 内容过少，缺乏关键指导
- 缺少 scheduled reminders 说明
- 缺少 heartbeat tasks 说明

#### TOOLS.md 差异
**原项目内容：**
- 简洁的工具安全限制说明
- 引用 cron skill 处理定时任务
- 更实用的约束信息

**当前项目内容：**
- 过于详细的工具签名展示
- 包含不必要的技术细节
- 缺乏实际使用约束

### 3. HEARTBEAT.md 实现差异

**原项目实现：**
- **HeartbeatService 类**：完整的周期性服务实现
- **两阶段处理**：
  1. Phase 1：读取 HEARTBEAT.md，通过 LLM 判断是否有任务
  2. Phase 2：如有任务，执行完整的 agent loop 并通知结果
- **虚拟工具调用**：使用 `heartbeat` 工具让 LLM 判断是否有活跃任务
- **30分钟间隔**：可配置的检查间隔

**当前项目缺失：**
- 无 HeartbeatService 实现
- HEARTBEAT.md 仅作为模板文件存在
- 无周期性任务执行逻辑

### 4. History/Memory 实现差异

**原项目实现：**
- **双层记忆系统**：
  - `MEMORY.md`：长期事实存储
  - `HISTORY.md`：grep 可搜索的事件日志
- **MemoryStore 类**：
  - `read_long_term()` / `write_long_term()`：管理长期记忆
  - `append_history()`：追加历史事件
  - `consolidate()`：通过 LLM 压缩旧消息到记忆文件
- **会话压缩**：定期将旧消息总结到记忆中，保持对话上下文简洁

**当前项目缺失：**
- 无 MemoryStore 实现
- 无 HISTORY.md 管理
- 无记忆压缩逻辑

### 5. Sessions 实现差异

**原项目实现：**
- **SessionManager 类**：完整的会话管理
- **JSONL 格式存储**：每行一个 JSON 对象，便于追加
- **会话迁移**：支持从旧位置迁移会话文件
- **消息去重**：确保消息历史不重复
- **元数据管理**：包含创建时间、更新时间等元信息

**当前项目缺失：**
- 无 SessionManager 实现
- 无会话持久化逻辑
- 无消息历史管理

## 需要更新的内容

### 1. 目录结构调整
```
src/
├── templates/              # 原 workspace/ 改为 templates/
│   ├── AGENTS.md          # 更新内容与原项目一致
│   ├── SOUL.md            # 保持不变
│   ├── TOOLS.md           # 更新内容与原项目一致
│   ├── USER.md            # 保持不变
│   └── HEARTBEAT.md       # 保持不变
├── NanoBot.Core/
│   ├── Workspace/         # 重命名和调整
│   ├── Memory/            # 新增：MemoryStore 实现
│   └── Sessions/          # 新增：SessionManager 实现
├── NanoBot.Agent/
│   ├── Context/           # 增强：ContextBuilder 实现
│   └── Heartbeat/         # 新增：HeartbeatService 实现
```

### 2. 核心服务实现

#### MemoryStore (NanoBot.Core/Memory/)
- 双层记忆系统：MEMORY.md + HISTORY.md
- 记忆压缩和历史记录功能
- 异步操作支持

#### SessionManager (NanoBot.Core/Sessions/)
- JSONL 格式会话存储
- 会话生命周期管理
- 消息历史维护

#### HeartbeatService (NanoBot.Agent/Heartbeat/)
- 周期性任务检查
- LLM 驱动的任务判断
- 任务执行和通知

#### ContextBuilder (NanoBot.Agent/Context/)
- 模板文件加载
- 记忆上下文集成
- 技能渐进式加载

### 3. 模板内容更新

#### AGENTS.md
- 添加 scheduled reminders 说明
- 添加 heartbeat tasks 管理指导
- 增强工具使用指导

#### TOOLS.md
- 简化为关键约束和限制
- 移除冗余的工具签名展示
- 聚焦实际使用注意事项

### 4. 配置和依赖注入
- 更新 WorkspaceConfig 支持新的目录结构
- 在 DI 容器中注册新的服务
- 调整初始化逻辑

## 实施建议

### 优先级排序
1. **高优先级**：模板内容和目录结构调整（影响用户体验）
2. **中优先级**：MemoryStore 和 SessionManager（核心功能）
3. **低优先级**：HeartbeatService（增强功能）

### 实施步骤
1. 重命名 `src/workspace/` 为 `src/templates/`
2. 更新模板文件内容
3. 实现 MemoryStore 类
4. 实现 SessionManager 类
5. 实现 ContextBuilder 增强
6. 实现 HeartbeatService
7. 更新 DI 配置和初始化逻辑
8. 测试和验证

## 结论

当前 NanoBot.Net 的 workspace 实现相对简化，缺少原项目的关键功能。通过此次更新，将显著提升系统的完整性和用户体验，使其更好地贴近原项目的设计理念。
