# Templates 与 Skills 使用指南

本文档说明 NanoBot Agent 如何使用 Templates（模板）和 Skills（技能）来扩展其能力。

## 概述

NanoBot Agent 通过 **AIContextProvider** 机制在每次 LLM 请求时动态注入上下文信息：

```
┌─────────────────────────────────────────────────────────────────┐
│                     AIContext Pipeline                          │
├─────────────────────────────────────────────────────────────────┤
│  1. BootstrapContextProvider → AGENTS.md, SOUL.md, USER.md,    │
│                                   TOOLS.md                      │
│  2. MemoryContextProvider    → memory/MEMORY.md (长期记忆)       │
│  3. SkillsContextProvider    → Skills 列表 + Always Skills 内容  │
│  4. Current Time            → 当前时间信息                      │
└─────────────────────────────────────────────────────────────────┘
```

## 目录结构

```
workspace/
├── AGENTS.md      # Agent 配置和行为指令 (由 BootstrapContextProvider 加载)
├── SOUL.md        # 个性定义 (由 BootstrapContextProvider 加载)
├── USER.md        # 用户画像 (由 BootstrapContextProvider 加载)
├── TOOLS.md       # 工具使用指南 (由 BootstrapContextProvider 加载)
├── HEARTBEAT.md   # 心跳任务列表 (HeartbeatService 定期检查)
├── skills/        # 自定义技能目录
│   ├── skill-name/
│   │   └── SKILL.md
│   └── .../
├── memory/
│   ├── MEMORY.md      # 长期记忆文件
│   └── HISTORY.md     # 历史记录摘要
└── sessions/         # 会话历史 (JSONL 格式)
```

---

## Templates（模板）

### 模板文件列表

| 文件 | 用途 | 加载方式 | 描述 |
|------|------|----------|------|
| `AGENTS.md` | Agent 指令 | BootstrapContextProvider | 定义工具调用规则、指南、可用工具列表 |
| `SOUL.md` | 个性定义 | BootstrapContextProvider | 定义人格特征、价值观、沟通风格 |
| `USER.md` | 用户画像 | BootstrapContextProvider | 用户基本信息、偏好、技术水平 |
| `TOOLS.md` | 工具指南 | BootstrapContextProvider | 工具使用的补充说明和安全限制 |
| `HEARTBEAT.md` | 心跳任务 | HeartbeatService | 周期性任务列表，每 30 分钟检查 |

### BootstrapContextProvider 加载机制

位置: `src/NanoBot.Agent/Context/BootstrapContextProvider.cs`

```csharp
// 核心常量定义
public static class Bootstrap
{
    public const string AgentsFile = "AGENTS.md";
    public const string SoulFile = "SOUL.md";
    public const string UserFile = "USER.md";
    public const string ToolsFile = "TOOLS.md";
    public static readonly string[] AllFiles = [AgentsFile, SoulFile, UserFile, ToolsFile];
}
```

**加载流程:**
1. 每次 LLM 请求时，BootstrapContextProvider 被调用
2. 按顺序读取所有 Bootstrap 文件
3. 5 分钟缓存机制，减少文件系统访问
4. 内容被包装成 Markdown section 注入到系统提示词

**代码实现:**

```csharp
protected override async ValueTask<AIContext> ProvideAIContextAsync(
    InvokingContext context,
    CancellationToken cancellationToken)
{
    // 检查缓存
    if (_cacheTime + _cacheDuration > DateTime.UtcNow && _cachedInstructions != null)
        return new AIContext { Instructions = _cachedInstructions };

    var instructions = new StringBuilder();

    foreach (var fileName in BootstrapFiles)
    {
        var filePath = GetFilePath(fileName);
        if (!File.Exists(filePath)) continue;

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(content))
        {
            var sectionName = GetSectionName(fileName);
            instructions.AppendLine($"## {sectionName}");
            instructions.AppendLine(content);
            instructions.AppendLine();
        }
    }

    _cachedInstructions = instructions.ToString();
    return new AIContext { Instructions = _cachedInstructions };
}
```

### 模板文件详解

#### AGENTS.md
Agent 的核心指令文件，包含：
- 工具调用规范（强制使用工具而非仅描述）
- 执行流程（调用 → 执行 → 报告）
- 可用工具列表（exec, browser, cron, message, spawn, rpa 等）
- 心跳任务处理指南

#### SOUL.md
定义 Agent 的人格特征：
```markdown
# Soul

I am nanobot 🐈, a personal AI assistant.

## Personality
- Helpful and friendly
- Concise and to the point
- Curious and eager to learn

## Values
- Accuracy over speed
- User privacy and safety
- Transparency in actions

## Communication Style
- Be clear and direct
- Explain reasoning when helpful
- Ask clarifying questions when needed
```

#### USER.md
用户画像模板，用于个性化交互：
- 基本信息（姓名、时区、语言）
- 沟通风格偏好
- 技术水平
- 工作上下文
- 特殊指令

#### TOOLS.md
工具使用补充说明：
- exec 安全限制
- cron 使用方式
- browser 最佳实践

#### HEARTBEAT.md
心跳任务列表：
```markdown
# Heartbeat Tasks

This file is checked every 30 minutes by your nanobot agent.

## Active Tasks

<!-- Add your periodic tasks below this line -->


## Completed

<!-- Move completed tasks here or delete them -->
```

---

## Skills（技能）

### Skill 系统概述

Skills 是 NanoBot 从 [OpenClaw](https://github.com/openclaw/openclaw) 适配的模块化扩展机制。每个 Skill 是自包含的功能包，通过 YAML frontmatter + Markdown 指令格式定义。

### Skill 结构

```
skill-name/
├── SKILL.md (必需)    # YAML frontmatter + 使用说明
├── scripts/           # 可选：可执行脚本
├── references/        # 可选：参考文档
└── assets/            # 可选：资源文件
```

### SKILL.md 格式

```markdown
---
name: skill-name
description: 技能描述，用于 LLM 判断何时使用此技能
always: true|false     # 是否总是加载到上下文
homepage: https://...  # 技能相关链接
metadata:              # 扩展元数据
  nanobot:
    emoji: "🧵"
    requires:
      bins: ["gh", "curl"]
      env: ["API_KEY"]
    install:
      - id: brew
        kind: brew
        formula: gh
        bins: ["gh"]
        label: "Install GitHub CLI"
---

# Skill Title

技能详细使用说明...

## Quick Start

使用示例...

## Advanced

高级用法...
```

### 技能元数据字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | string | 技能唯一标识 |
| `description` | string | 技能描述，用于 LLM 触发判断 |
| `always` | boolean | 是否总是加载到上下文 |
| `homepage` | string | 相关文档链接 |
| `metadata.nanobot.emoji` | string | 技能图标 |
| `metadata.nanobot.requires.bins` | string[] | 需要的命令行工具 |
| `metadata.nanobot.requires.env` | string[] | 需要的环境变量 |
| `metadata.nanobot.install` | array | 安装指引 |

### 内置 Skills

NanoBot 包含以下内置 Skills（位于 `src/skills/`）:

| Skill | 描述 | 依赖 |
|-------|------|------|
| `memory` | 双层记忆系统 | 无 |
| `summarize` | 总结 URL、文件、YouTube 视频 | `summarize` CLI |
| `weather` | 天气查询 | `curl` |
| `github` | GitHub 交互 | `gh` CLI |
| `cron` | 定时任务调度 | 无 |
| `tmux` | tmux 会话控制 | `tmux` |
| `skill-creator` | 技能创建指南 | 无 |

### SkillsContextProvider 加载机制

位置: `src/NanoBot.Agent/Context/SkillsContextProvider.cs`

**加载流程:**

```csharp
protected override async ValueTask<AIContext> ProvideAIContextAsync(
    InvokingContext context,
    CancellationToken cancellationToken)
{
    await EnsureCacheAsync(cancellationToken);

    var instructions = new StringBuilder();

    // 1. 加载 Always Skills 的完整内容
    if (_cachedAlwaysSkills?.Count > 0)
    {
        instructions.AppendLine("# Active Skills");
        instructions.AppendLine(_cachedAlwaysSkillsContent);
    }

    // 2. 加载所有 Skills 的摘要列表
    if (!string.IsNullOrEmpty(_cachedSkillsSummary))
    {
        instructions.AppendLine("# Skills");
        instructions.AppendLine("To use a skill, read its SKILL.md file.");
        instructions.AppendLine(_cachedSkillsSummary);
    }

    return new AIContext { Instructions = instructions.ToString() };
}
```

**技能加载优先级:**

1. **Always Skills** (`always: true`): 总是加载完整内容到上下文
2. **Available Skills**: 加载摘要信息，供 LLM 决定何时使用
3. **Unavailable Skills**: 显示但标记为不可用，提示依赖安装

**Skills 摘要 XML 格式:**

```xml
<skills>
  <skill available="true">
    <name>github</name>
    <description>Interact with GitHub using gh CLI</description>
    <location>/path/to/skills/github/SKILL.md</location>
  </skill>
  <skill available="false">
    <name>weather</name>
    <description>Get weather info</description>
    <location>embedded:skills/weather/SKILL.md</location>
    <requires>CLI: curl</requires>
  </skill>
</skills>
```

### 技能来源

Skills 有两个来源：

1. **Workspace Skills** (`workspace`): 位于 `workspace/skills/` 目录
2. **Builtin Skills** (`builtin`): 编译到程序中的 `src/skills/` 目录

```csharp
// SkillsLoader.cs 加载逻辑
public async Task<IReadOnlyList<Skill>> LoadAsync(string directory, CancellationToken cancellationToken = default)
{
    var skills = new List<Skill>();

    // 1. 加载 Workspace Skills
    var workspaceSkillsPath = _workspaceManager.GetSkillsPath();
    if (Directory.Exists(workspaceSkillsPath))
    {
        foreach (var skillDir in Directory.GetDirectories(workspaceSkillsPath))
        {
            var skill = await LoadSkillFromDirectoryAsync(skillDir, "workspace", cancellationToken);
            // ...
        }
    }

    // 2. 加载 Builtin Skills (不覆盖 workspace 版本)
    var embeddedSkills = _resourceLoader.GetSkillsResourceNames()
        .Where(n => n.EndsWith("/SKILL.md"))
        .Select(n => n.Split('/')[1])
        .Distinct();

    foreach (var skillName in embeddedSkills)
    {
        if (skills.Any(s => s.Name == skillName)) continue; // workspace 优先
        var skill = await LoadSkillFromEmbeddedAsync(skillName, cancellationToken);
        // ...
    }

    return skills.AsReadOnly();
}
```

---

## 上下文注入完整流程

```
User Request
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│  CompositeAIContextProvider.ProvideAIContextAsync()          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  1. BootstrapContextProvider.InvokingAsync()                │
│     ├─ 读取 AGENTS.md                                       │
│     ├─ 读取 SOUL.md                                         │
│     ├─ 读取 USER.md                                         │
│     ├─ 读取 TOOLS.md                                        │
│     └─ 合并为 "## Agent Configuration / Personality / ..."  │
│                                                             │
│  2. MemoryContextProvider.InvokingAsync()                    │
│     ├─ 从 IMemoryStore 或 memory/MEMORY.md 加载              │
│     └─ 添加 "## Memory" section                             │
│                                                             │
│  3. SkillsContextProvider.InvokingAsync()                   │
│     ├─ 加载 Always Skills 完整内容                          │
│     ├─ 构建 Skills 摘要 XML                                  │
│     └─ 添加 "# Active Skills / # Skills" sections           │
│                                                             │
│  4. 添加时间信息                                             │
│     └─ "## Current Time\n2024-03-21 10:30 (Saturday)"        │
│                                                             │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
System Prompt = Instructions (工厂方法) + AIContext (Provider 注入)
    │
    ▼
LLM Request (Instructions + ChatHistory + Current Message)
    │
    ▼
LLM Response
```

---

## 内存管理与缓存

### BootstrapContextProvider 缓存

- 缓存时间: 5 分钟
- 缓存内容: 所有 Bootstrap 文件合并后的指令
- 失效条件: 缓存过期后重新读取

### SkillsContextProvider 缓存

- 缓存时间: 5 分钟
- 缓存内容:
  - `_cachedSkillsSummary`: 所有 Skills 的 XML 摘要
  - `_cachedAlwaysSkills`: Always Skills 名称列表
  - `_cachedAlwaysSkillsContent`: Always Skills 完整内容

### 最大指令长度限制

```csharp
// CompositeAIContextProvider 可配置最大指令字符数
if (_maxInstructionChars > 0 && result.Length > _maxInstructionChars)
{
    _logger?.LogWarning("[CONTEXT] Instructions truncated from {Original} to {Max} chars",
        result.Length, _maxInstructionChars);
    result = result[.._maxInstructionChars];
}
```

可通过配置 `memory.maxInstructionChars` 调整限制。

---

## 配置示例

### 启用/禁用特定模板

编辑 `workspace` 目录下的对应 `.md` 文件即可。不需要修改代码。

### 自定义 Skill

1. 在 `workspace/skills/` 创建技能目录
2. 编写 `SKILL.md` 文件
3. 可选添加 `scripts/`, `references/`, `assets/` 子目录

示例: `workspace/skills/my-skill/SKILL.md`

```markdown
---
name: my-skill
description: 我的自定义技能，用于处理特定任务
---

# My Skill

这个技能帮助处理...

## 使用方法

当用户说...
```

---

## 相关代码文件

| 文件 | 位置 | 说明 |
|------|------|------|
| `BootstrapContextProvider.cs` | `src/NanoBot.Agent/Context/` | Bootstrap 文件加载器 |
| `SkillsContextProvider.cs` | `src/NanoBot.Agent/Context/` | Skills 上下文加载器 |
| `MemoryContextProvider.cs` | `src/NanoBot.Agent/Context/` | 记忆上下文加载器 |
| `CompositeAIContextProvider.cs` | `src/NanoBot.Agent/NanoBotAgentFactory.cs` | 上下文提供者编排器 |
| `SkillsLoader.cs` | `src/NanoBot.Infrastructure/Skills/` | Skills 文件系统加载器 |
| `ISkillsLoader.cs` | `src/NanoBot.Core/Skills/` | Skills 加载器接口 |
| `AgentConstants.cs` | `src/NanoBot.Core/Constants/` | Bootstrap 文件常量 |
| `HeartbeatService.cs` | `src/NanoBot.Infrastructure/` | HEARTBEAT.md 监控服务 |

---

## 日志示例

启用日志后可以看到上下文加载情况:

```
[CONTEXT] Bootstrap: 1234 chars (5ms), Memory: 567 chars (2ms), Skills: 890 chars (3ms), Total context: 2691 chars (10ms)
```

- **Bootstrap**: 来自 AGENTS.md, SOUL.md, USER.md, TOOLS.md
- **Memory**: 来自 memory/MEMORY.md
- **Skills**: 来自 Skills 的摘要和 Always Skills 内容
