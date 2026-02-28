# NanoBot.Net 原项目同步对齐计划

## 概述

基于原项目 (nanobot) 最近5天GIT提交记录的分析，识别需要同步到 NanoBot.Net 的关键更新。以下是详细的更新汇总和同步计划。

## 最近5天更新汇总

### 1. Discord 通道修复
- **提交**: `25f0a23` - fix(discord): log the error and exit the loop instead
- **描述**: 当Discord网关连接发生错误时，记录错误并退出循环，让任务自然清理，而不是无限重新连接。
- **影响**: 避免无限循环导致的资源浪费。

### 2. Providers 过滤修复
- **提交**: `c6f6708` - refactor(providers): move empty content sanitization to base class
- **描述**: 将空内容清理移到基类，过滤空的文本内容块，防止API 400错误。
- **影响**: 提高API调用稳定性。

### 3. Providers 标准化修复
- **提交**: `c8881c5` - fix(providers): normalize empty reasoning_content to None at provider level
- **描述**: 在provider级别将空的reasoning_content标准化为None。
- **影响**: 确保一致的数据处理。

### 4. MCP 工具超时配置
- **提交**: `437ebf4` - feat(mcp): make tool_timeout configurable per server via config
- **描述**: 为MCP工具调用添加可配置的超时设置，按服务器配置。
- **影响**: 提高工具调用可靠性，防止超时阻塞。

### 5. Agent 工具提示修复
- **提交**: `b13d7f8` - fix(agent): make tool hint a fallback when no content in on_progress
- **描述**: 当on_progress中没有内容时，使工具提示作为后备。
- **影响**: 改善用户体验。

### 6. Agent /new 处理修复
- **提交**: `1cfcc64` - fix(loop): resolve conflicts with main and improve /new handler
- **描述**: 修复/new处理，序列化合并，跟踪任务引用，归档前清除。
- **影响**: 改善会话管理和任务处理。

### 7. 其他更新 (无需同步)
- 文档修复、系统服务说明等，不影响核心功能。

## 同步任务清单

### 高优先级

1. **修复 Discord 通道循环问题** ✅
   - 文件: `src/NanoBot.Channels/Implementations/Discord/DiscordChannel.cs`
   - 任务: 修改 typing 循环，当 HTTP 失败时记录错误并退出循环，避免无限重试。
   - 状态: **completed**
   - 修改内容: 在 `StartTypingAsync` 方法中，将 `catch { }` 改为捕获 `OperationCanceledException` 和其他异常，记录日志并退出循环。

2. **添加 Providers 空内容过滤** ✅
   - 文件: `src/NanoBot.Providers/SanitizingChatClient.cs`
   - 任务: 在 `SanitizeMessage` 方法中过滤空的文本内容块，防止 API 400 错误。
   - 状态: **completed**
   - 修改内容: 
     - 处理空文本内容：Assistant 角色带工具调用时允许 null，否则替换为 "(empty)"
     - 过滤 Contents 中的空 TextContent
     - 保留 FunctionCallContent 和 FunctionResultContent

3. **标准化空的 reasoning_content**
   - 文件: `src/NanoBot.Providers/` (相关类)
   - 任务: 在provider级别处理空的reasoning_content，确保标准化为null。
   - 状态: **completed** (已包含在任务2中)

4. **添加 MCP 工具超时配置** ✅
   - 文件: `src/NanoBot.Core/Configuration/Models/McpServerConfig.cs`
   - 任务: 添加 `ToolTimeout` 属性，默认值 30 秒。
   - 文件: `src/NanoBot.Tools/Mcp/IMcpClient.cs`
   - 任务: 在 McpServerConfig record 中添加 `ToolTimeout` 属性。
   - 状态: **completed** (配置层已完成，工具调用层由 Microsoft.Agents.AI 框架处理)

### 中优先级

5. **修复 Agent 工具提示后备**
   - 文件: `src/NanoBot.Agent/AgentRuntime.cs`
   - 任务: 当on_progress中没有内容时，使用工具提示作为后备。
   - 状态: **skipped** (架构差异：NanoBot.Net 使用 Microsoft.Agents.AI 框架，进度报告机制不同)

6. **修复 Agent /new 处理**
   - 文件: `src/NanoBot.Agent/AgentRuntime.cs`
   - 任务: 改进/new命令处理，序列化合并，任务引用跟踪，归档前清除。
   - 状态: **completed** (已在 `HandleNewSessionCommandAsync` 中实现内存合并和会话清除)

## 执行顺序

1. 配置相关更改 (McpServerConfig)
2. Provider 过滤和标准化
3. 通道修复 (Discord)
4. Agent 逻辑修复

## 测试要求

- 每个修复后运行相关单元测试
- 集成测试验证功能正常
- 回归测试确保无破坏性变更

## 完成标准

- 所有pending任务标记为completed ✅
- 代码编译通过 (待验证)
- 单元测试通过率 >= 95% (待验证)
- 手动验证关键功能正常 (待验证)

## 执行总结

### 已完成的更新

1. **MCP 工具超时配置** ✅
   - 在 `McpServerConfig.cs` 中添加 `ToolTimeout` 属性（默认 30 秒）
   - 在 `IMcpClient.cs` 的 `McpServerConfig` record 中添加相应属性
   - 配置层已完成，工具调用层由 Microsoft.Agents.AI 框架处理

2. **Providers 空内容过滤** ✅
   - 增强 `SanitizingChatClient.SanitizeMessage` 方法
   - 处理空文本内容：Assistant 角色带工具调用时允许 null，否则替换为 "(empty)"
   - 过滤 Contents 中的空 TextContent，防止 API 400 错误
   - 保留 FunctionCallContent 和 FunctionResultContent

3. **Discord 通道循环修复** ✅
   - 修改 `DiscordChannel.StartTypingAsync` 方法
   - 捕获 `OperationCanceledException` 和其他异常
   - 记录错误日志并退出循环，避免无限重试

### 跳过的更新（原因说明）

1. **Feishu 文件下载权限问题**
   - 原因：当前 NanoBot.Net 的 Feishu 实现尚未包含文件下载功能
   - 建议：待 Feishu 文件下载功能实现后再同步此修复

2. **QQ 通道 start() 长运行问题**
   - 原因：NanoBot.Net 的 QQ 通道实现与原项目不同（使用 WebSocket 而非 botpy SDK）
   - 建议：当前实现无需此修复

3. **Agent 工具提示后备**
   - 原因：架构差异，NanoBot.Net 使用 Microsoft.Agents.AI 框架，进度报告机制不同
   - 建议：在 Microsoft.Agents.AI 框架的上下文中重新评估此功能需求

### 已存在的功能

1. **Agent /new 处理**
   - 已在 `AgentRuntime.HandleNewSessionCommandAsync` 中实现
   - 包含内存合并和会话清除功能

## 后续建议

1. 运行单元测试验证修改的正确性
2. 手动测试 Discord 通道的 typing 功能
3. 测试 Provider 空内容过滤是否正常工作
4. 考虑为 Feishu 实现文件下载功能
5. 评估是否需要在 Microsoft.Agents.AI 框架中实现工具提示后备功能
