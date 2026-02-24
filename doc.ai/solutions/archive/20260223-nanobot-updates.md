# Nanobot 项目更新报告 (2026-02-20 ~ 2026-02-23)

本报告汇总了原项目 nanobot 最近3天的Git更新内容。

---

## 更新概览

| 日期 | 提交数 | 主要内容 |
|------|--------|----------|
| 2026-02-23 | 8 | Agent可靠性改进、模板重构、默认参数调整 |
| 2026-02-22 | 6 | Provider重构、Channel功能增强、Heartbeat修复 |
| 2026-02-20~21 | - | 多个历史PR合并 |

---

## 详细更新内容

### 1. Agent 可靠性改进 (PR #1046)

**提交**: `d946228` - improve agent reliability: behavioral constraints, full tool history, error hints

**修改文件**:
- `nanobot/agent/context.py` - 20行增减
- `nanobot/agent/loop.py` - 49行增减
- `nanobot/agent/tools/registry.py` - 13行增减
- `nanobot/config/schema.py` - 4行增减
- `README.md` - 2行增减

**内容**: 增强了Agent的行为约束、完整的工具历史记录和错误提示功能。

---

### 2. 默认温度参数调整

**提交**: `4917392` - fix: lower default temperature from 0.7 to 0.1

**修改文件**:
- `nanobot/agent/loop.py`
- `nanobot/config/schema.py`

**内容**: 将默认temperature从0.7降低到0.1，使模型输出更确定性。

---

### 3. 模板目录重构 (PR #1043)

**提交**: `577b3d1` - refactor: move workspace/ to nanobot/templates/ for packaging

**修改文件**:
- `README.md` - +20行
- `nanobot/cli/commands.py` - 重构
- `nanobot/templates/` - 新目录，包含所有模板文件
  - `AGENTS.md`
  - `HEARTBEAT.md`
  - `SOUL.md`
  - `TOOLS.md`
  - `USER.md`
  - `memory/MEMORY.md`

**内容**: 将 `workspace/` 目录迁移到 `nanobot/templates/`，便于打包分发。

---

### 4. Heartbeat 修复 (PR #1036)

**提交**: `9025c70` - fix(heartbeat): route heartbeat runs to enabled chat context

**修改文件**:
- `nanobot/cli/commands.py` - +27行/-5行

**内容**: 修复heartbeat运行时的上下文路由问题，确保心跳事件发送到启用的聊天上下文。

---

### 5. 内存合并触发条件修复

**提交**: `bc32e85` - fix(memory): trigger consolidation by unconsolidated count, not total

**修改文件**:
- `nanobot/agent/loop.py` - +2行/-1行

**内容**: 修复内存合并触发逻辑，现在基于未合并计数触发而非总数。

---

### 6. Channel 功能增强 (PR #1000)

**提交**: `df2c837` - feat(channels): split send_progress into send_progress + send_tool_hints

**修改文件**:
- `nanobot/agent/loop.py` - +12行/-5行
- `nanobot/channels/manager.py` - +7行/-2行
- `nanobot/cli/commands.py` - +19行/-2行
- `nanobot/config/schema.py` - +3行/-1行

**内容**: 将 `send_progress` 拆分为 `send_progress` 和 `send_tool_hints` 两个独立选项，增强对进度消息传输的控制。

---

### 7. Provider 重构 (PR #949)

**提交**: `b653183` - refactor(providers): move empty content sanitization to base class

**修改文件**:
- `nanobot/providers/base.py` - +40行
- `nanobot/providers/custom_provider.py` - -45行
- `nanobot/providers/litellm_provider.py` - +2行/-1行

**内容**: 将空内容清理逻辑从具体Provider移动到基类中，减少代码重复。

---

## 对 NanoBot.Net 移植的影响分析

### 需要关注的更新

1. **默认温度参数** (`temperature: 0.1`)
   - 需要在配置schema中更新默认值

2. **模板目录重构** (`nanobot/templates/`)
   - 资源加载逻辑需要调整路径引用

3. **Channel send_tool_hints**
   - 需要在Channel接口中添加 `send_tool_hints` 属性

4. **Provider基类重构**
   - 空内容清理逻辑应该在基类实现，避免在各Provider中重复

---

## 更新文件列表

```
b2a1d12 Merge PR #1046 to improve agent reliability
d946228 improve agent reliability: behavioral constraints...
4917392 fix: lower default temperature from 0.7 to 0.1
e69ff8a Merge pull request #1043 to move workspace/...
577b3d1 refactor: move workspace/ to nanobot/templates/...
f8e8cbe Merge PR #1036: fix(heartbeat): route heartbeat...
e437689 Merge remote-tracking branch 'origin/main' into pr-1036
0fdbd5a Merge PR #1000: feat(channels): add send_progress...
df2c837 feat(channels): split send_progress into send_progress...
c20b867 Merge remote-tracking branch 'origin/main' into pr-1000
bc32e85 fix(memory): trigger consolidation by unconsolidated...
9025c70 fix(heartbeat): route heartbeat runs to enabled...
31a873c Merge branch 'main' of https://github.com/HKUDS/nanobot
0c412b3 feat(channels): add send_progress option...
25f0a23 docs: fix MiniMax API key link
c6f6708 Merge PR #949: fix(provider): filter empty text...
b653183 refactor(providers): move empty content sanitization...
```

---

*报告生成时间: 2026-02-23*
