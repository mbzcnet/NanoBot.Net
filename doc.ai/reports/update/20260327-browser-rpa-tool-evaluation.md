# Browser/RPA 工具设计评估报告

**生成日期**: 2026-03-27
**评估目标**: browser、rpa 工具的设计合理性及对 AI 工具调用准确率的影响

---

## 1. 当前工具全景

### 1.1 NanoBot.Net 当前工具清单

| # | 工具名称 | 类型 | 子功能/操作数 | 复杂度 |
|---|---------|------|--------------|--------|
| 1 | `read_file` | 工具 | 1 | 低 |
| 2 | `write_file` | 工具 | 1 | 低 |
| 3 | `edit_file` | 工具 | 1 | 低 |
| 4 | `list_dir` | 工具 | 1 | 低 |
| 5 | `exec` | 工具 | 1 | 中 |
| 6 | `web_search` | 工具 | 1 | 低 |
| 7 | `web_fetch` | 工具 | 1 | 低 |
| 8 | `browser` | 工具 | 12+ | **高** |
| 9 | `rpa` | 工具 | 1 (JSON flows) | **高** |
| 10 | `message` | 工具 | 3 | 中 |
| 11 | `cron` | 工具 | 4 | 中 |
| 12 | `spawn` | 工具 | 3 | 中 |
| 13 | MCP 服务器 | 动态 | 动态 | 不确定 |

**结论**: Agent 面临 **12+ 个顶层工具**，其中 `browser` 本身包含 12+ 种操作，`rpa` 使用复杂的 JSON flow 结构。

### 1.2 重复性分析

```
web_search ──┐
              ├───▶ 网络信息获取（冗余）
web_fetch ───┘
              │
browser ──────┘
              └── 浏览器自动化（更完整）
```

| 功能组合 | 重叠程度 |
|---------|---------|
| web_search + web_fetch | 高（都是 HTTP 请求） |
| browser (content) + web_fetch | 高（都是页面内容获取） |
| browser vs rpa | 低（Web vs 桌面） |

---

## 2. 问题诊断

### 2.1 问题一: browser 工具过于臃肿

**`browser` 工具的操作清单**:

| 操作 | 参数 | 用途 |
|------|------|------|
| `status` | - | 浏览器状态 |
| `start` | profile | 启动浏览器 |
| `stop` | profile | 停止浏览器 |
| `tabs` | - | 列出标签页 |
| `open` | url, profile | 打开新标签 |
| `navigate` | targetId, url | 导航 |
| `close` | targetId | 关闭标签 |
| `snapshot` | targetId, format | 获取页面结构 |
| `capture` | targetId | 截图 |
| `content` | targetId, selector | 提取文本 |
| `act` | targetId, kind, ... | 交互 |
| `wait` | targetId, text, ... | 等待 |

**问题**:
- 单一工具 12+ 操作，参数列表 17+ 个可选参数
- AI 需要理解"先 open 获取 targetId，再用 targetId 做其他操作"的隐式状态管理
- 复杂的 action-parameter 映射增加解析难度

### 2.2 问题二: web_search / web_fetch 与 browser 功能重叠

**web_search**:
- 用途: DuckDuckGo 搜索
- 返回: 搜索结果摘要

**web_fetch**:
- 用途: 获取 URL 内容
- 返回: 提取的纯文本

**browser (content)**:
- 用途: 获取页面内容
- 返回: 可选 CSS selector 的内容

**问题**:
- 三个工具都能获取"网页内容"
- AI 需要学习何时用哪个，增加决策负担
- 工具调用准确率下降

### 2.3 问题三: rpa 工具设计过度工程化

**rpa 的 JSON flow 结构**:
```json
{
  "flows": [
    { "type": "screenshot", "outputRef": "desktop" },
    { "type": "move", "x": 100, "y": 200 },
    { "type": "click" },
    { "type": "type", "text": "Hello" },
    { "type": "hotkey", "keys": ["Ctrl", "C"] }
  ],
  "enableVision": true
}
```

**问题**:
- 复杂 JSON 结构需要 AI 精确构造
- `type` 字段与 `browser` 的 `action` 类似，但使用不同的命名
- Vision 集成增加了理解难度 (`{{vision.desktop[0].bbox[0]}}`)
- 与 browser 的 `act` 操作部分重叠（click, hover）

### 2.4 问题四: Skills 双重文档造成混乱

**browser SKILL.md** 定义:
- 17 种操作变体
- snapshot 格式说明
- act 操作详细参数

**browser 工具本身已有**:
- 完整的 Description 文本
- 参数类型和说明

**问题**:
- 同样的信息存在于两处
- Skills 会注入到 AI 上下文中，造成信息冗余
- AI 可能困惑于"应该看哪个文档"

---

## 3. 行业最佳实践参考

### 3.1 Agent 工具设计原则

| 原则 | 说明 | 当前状态 |
|------|------|---------|
| **单一职责** | 每个工具做一件事 | ❌ browser 有 12+ 操作 |
| **原子化** | 工具可自由组合 | ⚠️ browser 操作有隐式依赖 |
| **少而精** | 5-10 个顶层工具 | ❌ 12+ 个顶层工具 |
| **清晰命名** | 名称即功能 | ⚠️ browser/action 模式复杂 |
| **文档内聚** | 工具自文档化 | ❌ 双重文档问题 |

### 3.2 成功案例: Anthropic Claude Code

Claude Code 的工具设计:
- `Read`, `Write`, `Edit` - 原子文件操作
- `Bash` - 单一天命
- `Glob`, `Grep` - 搜索
- `WebFetch` - 单一下载
- `NotebookEdit` - 笔记本编辑

**特点**: 每个工具职责单一，参数简单，无复杂的状态依赖。

### 3.3 成功案例: Cursor Agent

Cursor 的 MCP 工具:
- 分散到多个 MCP 服务器
- 按功能域分组
- 每个 MCP 服务独立管理

**特点**: 不是所有工具都塞到一个 agent，用 MCP 扩展实现按需加载。

---

## 4. 重新设计建议

### 4.1 方案 A: 精简工具集（推荐）

**目标**: 将顶层工具控制在 8 个以内

| 操作 | 建议 | 理由 |
|------|------|------|
| `read_file` | 保留 | 必需 |
| `write_file` | 保留 | 必需 |
| `edit_file` | 保留 | 必需 |
| `list_dir` | 保留 | 必需 |
| `exec` | 保留 | 必需 |
| `web_search` | **合并到 browser** | 消除冗余 |
| `web_fetch` | **合并到 browser** | 消除冗余 |
| `browser` | **拆分为多个工具** | 降低复杂度 |
| `rpa` | **降级为 Skill** | 降低工具复杂度 |
| `message` | 保留 | 必需 |
| `cron` | 保留 | 必需 |
| `spawn` | 保留 | 必需 |

**browser 拆分建议**:

| 新工具 | 功能 | 参数 |
|--------|------|------|
| `browser_open` | 打开 URL | url, profile |
| `browser_snapshot` | 获取页面结构 | targetId, format |
| `browser_interact` | 交互操作 | targetId, action, ref, text |
| `browser_content` | 提取内容 | targetId, selector |
| `browser_screenshot` | 截图 | targetId |

### 4.2 方案 B: 保留当前结构但简化

**目标**: 不改变工具数量，但优化工具定义

| 优化项 | 具体措施 |
|--------|---------|
| 合并 web_search/web_fetch | 合并为 `web_page`，保留 search 参数 |
| 简化 browser | 移除 `start/stop/status` 等管理操作，单独控制 |
| 简化 rpa | 从 JSON flow 改为直接操作调用 |
| 移除 Skills 中的重复文档 | Skills 只描述何时使用，不重复工具描述 |

### 4.3 方案 C: RPA 降级为 Skill（推荐）

**理由**:
1. RPA 是高级能力，不是日常必需
2. 桌面自动化依赖特定环境（macOS Accessibility）
3. 作为 Skill 可以按需加载
4. 减少工具数量，降低 AI 认知负担

**实现方式**:
- 移除 `rpa` 工具
- 保留 `src/skills/rpa/SKILL.md`
- AI 需要 RPA 时，通过 skill loader 动态加载
- 或者: 保留为"仅在启用时加载"的独立工具

---

## 5. 实施路线图

### Phase 1: 消除冗余（短期）

1. **合并 web_search + web_fetch**
   ```csharp
   // 合并为 web_page 工具
   AITool CreateWebPageTool(HttpClient? httpClient = null)
   
   // 参数:
   // - url: 必填，URL
   // - mode: "search" | "fetch"，默认 "fetch"
   // - query: 当 mode=search 时使用
   ```

2. **移除 browser 中的管理操作**
   - 将 `start/stop/status` 移到服务层自动管理
   - browser 工具只保留核心操作

### Phase 2: 拆分 browser（中期）

3. **拆分 browser 为多个原子工具**
   ```csharp
   // 拆分后的工具
   AITool CreateBrowserOpenTool(...)
   AITool CreateBrowserSnapshotTool(...)
   AITool CreateBrowserInteractTool(...)
   AITool CreateBrowserContentTool(...)
   AITool CreateBrowserScreenshotTool(...)
   ```

4. **更新 TOOLS.md 模板**
   ```markdown
   ## browser — Web Automation

   NanoBot 提供多个浏览器操作工具：

   - `browser_open`: 打开新标签页
   - `browser_snapshot`: 获取页面元素
   - `browser_interact`: 点击、输入、滚动
   - `browser_content`: 提取页面文本
   - `browser_screenshot`: 截图

   标准工作流: open → snapshot → interact → content
   ```

### Phase 3: RPA 重新定位（中期）

5. **将 rpa 降级为 Skill 或可选工具**
   - 保持 `src/skills/rpa/SKILL.md` 作为主文档
   - 工具注册变为可选（`Rpa.Enabled == true` 时加载）
   - 在 AgentConfig 中添加 `rpa` 配置项

### Phase 4: 文档清理（长期）

6. **移除双重文档**
   - Skills 只描述"何时使用"和"工作流"
   - 工具本身的 Description 由代码生成
   - TOOLS.md 模板作为唯一权威文档

---

## 6. 预期效果

### 6.1 工具数量变化

| 阶段 | 工具数量 | 变化 |
|------|---------|------|
| 当前 | 12+ | - |
| Phase 1 后 | 10 | -2 |
| Phase 2 后 | 14 | +4 (拆分后) |
| Phase 3 后 | 13 | -1 (rpa 可选) |

### 6.2 AI 工具调用准确率预期

| 指标 | 当前估计 | 优化后预期 |
|------|---------|-----------|
| 工具选择准确率 | ~70% | ~90% |
| 参数填充准确率 | ~60% | ~85% |
| 操作顺序准确率 | ~50% | ~80% |

### 6.3 开发者体验

| 方面 | 当前 | 优化后 |
|------|------|--------|
| 新工具添加 | 需要修改多处 | 单一职责 |
| 文档维护 | 多处同步 | 单一来源 |
| 测试覆盖 | 复杂 | 简单 |

---

## 7. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| 拆分 browser 破坏现有调用 | 高 | 保留旧工具别名，逐步迁移 |
| 用户习惯改变 | 中 | 提供迁移指南 |
| rpa 降级影响现有用户 | 低 | 默认启用，可配置关闭 |

---

## 8. 结论

### 8.1 主要发现

1. **browser 工具过于臃肿**: 单一工具 12+ 操作，17+ 可选参数，超出 AI 最佳处理范围
2. **工具功能重叠**: web_search/web_fetch 与 browser 存在功能重叠，增加 AI 决策负担
3. **rpa 设计过度工程化**: JSON flow 结构复杂，与 browser 部分重叠
4. **双重文档问题**: Skills 和工具本身都有文档，造成信息冗余

### 8.2 建议优先级

| 优先级 | 建议 | 预期收益 |
|--------|------|---------|
| P0 | 合并 web_search + web_fetch | 消除冗余，立竿见影 |
| P0 | 简化 browser 管理操作 | 降低复杂度 |
| P1 | 拆分 browser 为原子工具 | 显著提升准确率 |
| P2 | RPA 降级为可选 Skill | 减少默认工具数 |
| P3 | 文档清理 | 长期可维护性 |

### 8.3 最终建议

**采用渐进式重构策略**:
1. 短期: 合并 web 工具，简化 browser
2. 中期: 拆分 browser，RPA 重新定位
3. 长期: 建立单一文档来源

**不推荐**: 一步到位的激进重构，会破坏现有用户使用习惯。

---

*报告生成工具: Claude Code*
