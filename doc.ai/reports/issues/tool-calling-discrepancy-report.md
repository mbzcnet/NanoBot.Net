# 工具调用问题分析报告

## 问题描述

在相同的模型、配置和用户消息条件下，工具调用在测试环境中通过，但在 CLI 和 WebUI 实际运行中却无法成功调用工具。

## 根因分析

经过代码审核，发现测试与实际运行环境存在以下关键差异：

### 1. 上下文提供者的差异（主要问题）

**测试环境** (`AgentRuntimeDiagnosticTests.cs`):
- 使用 Mock 的 `ISkillsLoader`，返回空技能列表
- **不加载** AGENTS.md、SOUL.md、USER.md、TOOLS.md 等引导文件
- **不加载** MEMORY.md 历史记忆
- 系统提示词仅包含基础指令 (`NanoBotAgentFactory.BuildInstructions`)

**实际运行环境** (`BootstrapContextProvider.cs` + `MemoryContextProvider.cs`):
- 加载 AGENTS.md → "Agent Configuration" 部分
- 加载 SOUL.md → "Personality" 部分  
- 加载 USER.md → "User Profile" 部分
- 加载 TOOLS.md → "Tools Guide" 部分（关键！）
- 加载 MEMORY.md → "Memory" 部分，包含历史对话摘要
- 添加运行时上下文（untrusted runtime context）

**影响**：
- TOOLS.md 中的工具使用指南可能包含误导性指令
- Memory 中的历史记录可能让 LLM 认为问题已经被回答过
- 过长的上下文可能稀释了工具调用指令的权重

### 2. 历史消息的差异

**测试环境**:
- 每个测试用例使用新的 session (`sessionKey: "test_session"`)
- 无历史消息，干净的上下文

**实际运行环境**:
- CLI 和 WebUI 都维护持久化的会话历史
- `SessionManager` 从磁盘加载历史消息
- `FileBackedChatHistoryProvider` 提供历史对话记录
- `MemoryConsolidationChatHistoryProvider` 可能注入总结性记忆

**影响**：
- 历史消息可能包含类似的查询，让 LLM 认为不需要调用工具
- 过长的历史上下文可能超出模型上下文窗口，导致指令被截断
- 历史中的错误示例可能引导 LLM 产生错误行为

### 3. 工具提供方式的差异

**测试环境** (`ToolCallingIntegrationTests.cs`):
- 使用 `AIFunctionFactory.Create` 创建简单的 mock 工具
- 工具描述简洁明了，无额外配置

**实际运行环境** (`ToolProvider.cs`):
- 通过 `ToolProvider.CreateDefaultToolsAsync` 创建工具
- 包含 10+ 个工具（文件、Shell、Web、浏览器、消息、Cron、Spawn 等）
- 工具描述可能过于复杂或相互重叠
- MCP 工具动态加载，描述可能不完整

**影响**：
- 工具数量过多可能导致 LLM 选择困难
- 工具描述质量不一致，某些工具可能被 LLM 忽略

### 4. Agent 配置的差异

**测试环境**:
- 使用简化的 `AgentOptions`
- 无 `maxInstructionChars` 限制
- 无内存存储 (`memoryStore: null`)

**实际运行环境** (`NanoBotAgentFactory.cs`):
- 使用 `CompositeAIContextProvider` 组合多个上下文源
- 可能有 `maxInstructionChars` 限制，导致指令被截断
- 启用内存存储和记忆整合

**影响**：
- 如果 `maxInstructionChars` 设置过小，关键的工具调用指令可能被截断
- 记忆整合过程可能错误地总结工具使用模式

## 具体代码位置

### 测试环境配置（无引导文件）
```csharp
// tests/NanoBot.Agent.Tests/Integration/AgentRuntimeDiagnosticTests.cs:288-297
private Mock<ISkillsLoader> CreateSkillsLoaderMock()
{
    var mock = new Mock<ISkillsLoader>();
    mock.Setup(s => s.GetAlwaysSkills()).Returns(new List<string>());
    mock.Setup(s => s.BuildSkillsSummaryAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(string.Empty);  // 返回空技能摘要！
    return mock;
}
```

### 实际环境的引导文件加载
```csharp
// src/NanoBot.Agent/Context/BootstrapContextProvider.cs:42-62
foreach (var fileName in BootstrapFiles)  // AGENTS.md, SOUL.md, USER.md, TOOLS.md
{
    var filePath = GetFilePath(fileName);
    if (!File.Exists(filePath)) continue;
    
    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
    // ... 添加到 instructions
}
```

### 实际环境的记忆加载
```csharp
// src/NanoBot.Agent/Context/MemoryContextProvider.cs:33-47
if (_memoryStore != null)
{
    memoryContent = await _memoryStore.LoadAsync(cancellationToken);
}
// 返回 AIContext with Instructions: $"## Memory\n\n{memoryContent}"
```

## 建议的诊断步骤

1. **比较提示词内容**
   - 在测试中添加日志，打印实际发送给 LLM 的完整提示词
   - 在 CLI/WebUI 中启用 debug 模式 (`--debug`) 对比提示词差异

2. **检查 TOOLS.md 内容**
   - 查看工作目录下的 TOOLS.md 是否包含冲突的工具使用指南
   - 确认是否有 "不要使用工具" 或 "直接回答" 等误导性指令

3. **验证历史消息影响**
   - 在 CLI 中使用 `/new` 创建新会话测试
   - 在 WebUI 中创建新会话测试
   - 对比有无历史消息时的工具调用行为

4. **检查指令截断**
   - 检查配置中 `maxInstructionChars` 的值
   - 确保工具描述和工具调用指令没有被截断

5. **工具描述优化**
   - 检查 `ToolProvider.CreateDefaultToolsAsync` 生成的工具描述
   - 确保每个工具的描述清晰、准确、无歧义

## 推荐的修复方案

### 方案 1: 添加工具调用强制指令（短期）
在 `NanoBotAgentFactory.BuildInstructions` 中明确添加工具调用优先级说明：

```csharp
sb.AppendLine("""
## Tool Calling Priority

When a user asks for information that can be obtained through tools:
1. ALWAYS prefer using tools over making up answers
2. Call the appropriate tool first, then provide the answer based on tool results
3. Do not rely on training data when a tool can provide fresh information

For example, if asked about file contents, use the read_file tool.
If asked about current information, use web_search or browser tools.
""");
```

### 方案 2: 上下文分级（中期）
在 `CompositeAIContextProvider.ProvideAIContextAsync` 中实现上下文优先级：
- 工具调用指令和当前工具列表优先级最高
- 用户当前消息次之
- 历史消息和记忆优先级最低
- 确保关键指令不被截断

### 方案 3: 会话隔离测试（诊断）
添加专门的集成测试，模拟实际运行环境的完整配置：
- 加载真实的引导文件
- 加载真实的历史消息
- 验证工具调用行为是否一致

## 相关文件

| 文件 | 作用 |
|------|------|
| `src/NanoBot.Agent/NanoBotAgentFactory.cs` | Agent 创建工厂，构建系统指令 |
| `src/NanoBot.Agent/Context/BootstrapContextProvider.cs` | 加载引导文件 |
| `src/NanoBot.Agent/Context/MemoryContextProvider.cs` | 加载记忆 |
| `src/NanoBot.Tools/ToolProvider.cs` | 创建工具列表 |
| `tests/NanoBot.Agent.Tests/Integration/AgentRuntimeDiagnosticTests.cs` | 诊断测试 |
| `tests/NanoBot.Agent.Tests/Integration/ToolCallingIntegrationTests.cs` | 工具调用测试 |

## 结论

问题的根本原因是**测试环境与实际运行环境的上下文构建存在显著差异**。测试使用了简化的配置，而实际运行加载了完整的引导文件、历史记忆和复杂的工具集合。这些额外的上下文可能：

1. 包含与工具调用冲突的指令
2. 稀释工具调用指令的权重
3. 导致指令被截断
4. 让 LLM 产生错误的行为模式

建议首先通过日志对比两个环境的实际提示词内容，确认具体的差异点，然后针对性地调整上下文构建逻辑或引导文件内容。
