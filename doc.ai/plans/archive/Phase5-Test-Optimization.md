# Phase 5: 测试优化计划

## 概述

本文档定义 NanoBot.Net 项目的测试优化方案。当前测试代码存在覆盖不足、测试深度不够、边界条件测试缺失等问题，需要进行全面优化以确保项目质量。

**目标**: 提升测试覆盖率至 75% 以上，增强测试的严谨性和可靠性。

**依赖**: Phase 4 应用层开发完成

---

## 一、当前测试问题分析

### 1.1 当前测试项目覆盖

| 测试项目 | 测试文件 | 测试数量 | 质量评估 |
|---------|---------|---------|---------|
| NanoBot.Tools.Tests | ToolsTests.cs, FileToolsExecutionTests.cs | 32 | 新增 FileTools 执行功能测试（19 个测试用例已通过） |
| NanoBot.Agent.Tests | AgentRuntimeTests.cs, SessionManagerTests.cs | 31 | 较完整 |
| NanoBot.Infrastructure.Tests | MessageBusTests.cs | 23 | 较完整 |
| NanoBot.Infrastructure.Tests | MemoryStoreTests.cs | 14 | 较完整 |
| NanoBot.Infrastructure.Tests | SkillsLoaderTests.cs | 16 | 较完整 |
| NanoBot.Infrastructure.Tests | CronServiceTests.cs | 12 | 较完整 |
| NanoBot.Infrastructure.Tests | HeartbeatServiceTests.cs | 14 | 较完整 |
| NanoBot.Cli.Tests | CommandTests.cs | 10 | 仅测试命令元数据 |
| NanoBot.Channels.Tests | 多个测试文件 | - | 部分实现 |
| NanoBot.Integration.Tests | 多个测试文件 | - | 部分实现 |

### 1.2 主要问题

1. **Tools 测试不完整**：
   - 仅测试 `CreateXxxTool()` 返回 `AIFunction` 类型
   - 缺少工具实际执行逻辑的测试
   - 缺少参数验证测试
   - 缺少边界条件测试

2. **Agent 核心测试缺失**：
   - Session 管理器的核心逻辑（GetOldMessages、合并）未测试
   - Context 构建逻辑未充分测试

3. **CLI 测试不足**：
   - 仅测试命令名称和描述
   - 缺少命令执行逻辑测试

4. **通道层测试覆盖不足**：
   - EmailChannel、TelegramChannel 等实现不完整

---

## 二、优化任务清单

### 任务组 1: Tools 层测试增强

#### 1.1 FileTools 功能测试 ✅ 已完成
- **任务**: 实现 FileTools 的完整功能测试
- **文件**: `tests/NanoBot.Tools.Tests/FileToolsExecutionTests.cs`
- **测试用例数量**: 22
- **状态**: 已完成

#### 1.2 ShellTools 功能测试 ⚠️ 部分完成
- **任务**: 实现 ShellTools 的完整功能测试
- **文件**: `tests/NanoBot.Tools.Tests/ShellToolsExecutionTests.cs`
- **测试用例数量**: 10
- **状态**: 测试代码已创建，但因平台差异（macOS/Windows 命令差异）需要调整

#### 1.3 WebTools 功能测试
- **任务**: 实现 WebTools 的完整功能测试
- **文件**: `tests/NanoBot.Tools.Tests/WebToolsTests.cs`
- **测试用例**:
  - `WebSearch_WithValidQuery_ReturnsResults`（需要 Mock HTTP）
  - `WebSearch_WithoutApiKey_ReturnsError`
  - `WebFetch_WithValidUrl_ReturnsContent`（需要 Mock HTTP）
  - `WebFetch_WithInvalidUrl_ReturnsError`
  - `WebFetch_WithMaxChars_TruncatesContent`
- **边界条件**:
  - 网络超时处理
  - HTTP 错误码处理（404, 500 等）
  - 重定向处理
  - 大页面内容截断

#### 1.4 MessageTools 功能测试
- **任务**: 实现 MessageTools 的完整功能测试
- **文件**: `tests/NanoBot.Tools.Tests/MessageToolsTests.cs`
- **测试用例**:
  - `SendMessage_WithValidParams_PublishesToBus`
  - `SendMessage_WithNullChannel_UsesDefault`
  - `SendMessage_WithNullChatId_UsesDefault`
- **边界条件**:
  - 空消息内容
  - 消息总线未启动

#### 1.5 CronTools 功能测试
- **任务**: 实现 CronTools 的完整功能测试
- **文件**: `tests/NanoBot.Tools.Tests/CronToolsTests.cs`
- **测试用例**:
  - `AddJob_WithValidSchedule_AddsJob`
  - `RemoveJob_WithExistingJob_RemovesJob`
  - `ListJobs_ReturnsAllJobs`
  - `EnableJob_TogglesJobState`
- **边界条件**:
  - 无效 Cron 表达式
  - 重复任务名称

#### 1.6 SpawnTools 功能测试
- **任务**: 实现 SpawnTools 的完整功能测试
- **文件**: `tests/NanoBot.Tools.Tests/SpawnToolsTests.cs`
- **测试用例**:
  - `Spawn_WithValidTask_CreatesSubagent`
  - `Spawn_WithLabel_UsesCustomLabel`
  - `Spawn_WithComplexTask_ReturnsResult`
- **边界条件**:
  - 空任务描述
  - 子 Agent 执行失败

#### 1.7 ToolValidationTests 参数验证测试
- **任务**: 实现工具参数验证测试
- **文件**: `tests/NanoBot.Tools.Tests/ToolValidationTests.cs`
- **测试用例**:
  - `ValidateParameters_WithMissingRequired_ReturnsErrors`
  - `ValidateParameters_WithTypeMismatch_ReturnsErrors`
  - `ValidateParameters_WithOutOfRange_ReturnsErrors`
  - `ValidateParameters_WithInvalidEnum_ReturnsErrors`
  - `ValidateParameters_WithValidParams_ReturnsSuccess`
  - `ValidateParameters_IgnoresUnknownFields`

#### 1.8 ToolRegistryTests 工具注册测试
- **任务**: 实现工具注册表测试
- **文件**: `tests/NanoBot.Tools.Tests/ToolRegistryTests.cs`
- **测试用例**:
  - `Register_AddsToolToRegistry`
  - `Register_WithDuplicateName_ThrowsException`
  - `GetTool_ReturnsRegisteredTool`
  - `GetTool_WithUnknownName_ReturnsNull`
  - `ListTools_ReturnsAllRegistered`
  - `Remove_RemovesToolFromRegistry`

---

### 任务组 2: CLI 层测试增强

#### 2.1 OnboardCommand 测试
- **任务**: 实现 OnboardCommand 的完整测试
- **文件**: `tests/NanoBot.Cli.Tests/OnboardCommandTests.cs`
- **测试用例**:
  - `Execute_WithFreshInstall_CreatesConfigAndWorkspace`
  - `Execute_WithExistingConfig_PromptsForOverwrite`
  - `Execute_WithExistingWorkspace_RefreshesTemplates`
  - `Execute_CreatesDefaultConfigFile`
  - `Execute_CreatesWorkspaceStructure`
- **边界条件**:
  - 权限不足无法创建目录
  - 磁盘空间不足

#### 2.2 AgentCommand 测试
- **任务**: 实现 AgentCommand 的完整测试
- **文件**: `tests/NanoBot.Cli.Tests/AgentCommandTests.cs`
- **测试用例**:
  - `Execute_StartsInteractiveMode`
  - `Execute_WithDirectMessage_ProcessesAndReturns`
  - `Execute_HandlesHelpCommand`
  - `Execute_HandlesNewCommand`
  - `Execute_HandlesExitCommand`
- **边界条件**:
  - 配置无效
  - LLM 连接失败

#### 2.3 StatusCommand 测试
- **任务**: 实现 StatusCommand 的完整测试
- **文件**: `tests/NanoBot.Cli.Tests/StatusCommandTests.cs`
- **测试用例**:
  - `Execute_ReturnsAgentInfo`
  - `Execute_WithJsonFlag_ReturnsJsonOutput`
  - `Execute_ShowsChannelStates`
  - `Execute_ShowsProviderInfo`

#### 2.4 ConfigCommand 测试
- **任务**: 实现 ConfigCommand 的完整测试
- **文件**: `tests/NanoBot.Cli.Tests/ConfigCommandTests.cs`
- **测试用例**:
  - `Get_ReturnsConfigValue`
  - `Set_UpdatesConfigValue`
  - `List_ReturnsAllConfig`
  - `Validate_ReturnsValidationResult`

#### 2.5 SessionCommand 测试
- **任务**: 实现 SessionCommand 的完整测试
- **文件**: `tests/NanoBot.Cli.Tests/SessionCommandTests.cs`
- **测试用例**:
  - `List_ReturnsAllSessions`
  - `Clear_RemovesSession`
  - `Show_DisplaysSessionContent`

#### 2.6 CronCommand 测试
- **任务**: 实现 CronCommand 的完整测试
- **文件**: `tests/NanoBot.Cli.Tests/CronCommandTests.cs`
- **测试用例**:
  - `Add_CreatesNewJob`
  - `Remove_DeletesJob`
  - `List_ReturnsAllJobs`
  - `Enable_TogglesJobState`
  - `Run_ExecutesJobImmediately`

#### 2.7 McpCommand 测试
- **任务**: 实现 McpCommand 的完整测试
- **文件**: `tests/NanoBot.Cli.Tests/McpCommandTests.cs`
- **测试用例**:
  - `Add_CreatesServerConfig`
  - `Remove_DeletesServerConfig`
  - `List_ReturnsAllServers`
  - `Connect_EstablishesConnection`
  - `Disconnect_ClosesConnection`

#### 2.8 ChannelsCommand 测试
- **任务**: 实现 ChannelsCommand 的完整测试
- **文件**: `tests/NanoBot.Cli.Tests/ChannelsCommandTests.cs`
- **测试用例**:
  - `List_ReturnsAllChannels`
  - `Enable_StartsChannel`
  - `Disable_StopsChannel`
  - `Status_ShowsChannelStatus`

#### 2.9 ProviderCommand 测试
- **任务**: 实现 ProviderCommand 的完整测试
- **文件**: `tests/NanoBot.Cli.Tests/ProviderCommandTests.cs`
- **测试用例**:
  - `List_ReturnsAllProviders`
  - `Set_SetsActiveProvider`
  - `Test_TestsProviderConnection`

---

### 任务组 3: Channels 层测试增强

#### 3.1 TelegramChannel 测试
- **任务**: 实现 TelegramChannel 的完整测试
- **文件**: `tests/NanoBot.Channels.Tests/TelegramChannelTests.cs`
- **测试用例**:
  - `StartAsync_ConnectsToTelegramApi`
  - `StopAsync_DisconnectsFromApi`
  - `SendMessageAsync_SendsToCorrectChat`
  - `OnMessageReceived_RaisesEvent`
  - `ConvertMarkdownToHtml_HandlesFormatting`
- **边界条件**:
  - 网络断开重连
  - 无效 Token
  - 发送消息失败

#### 3.2 DiscordChannel 测试
- **任务**: 实现 DiscordChannel 的完整测试
- **文件**: `tests/NanoBot.Channels.Tests/DiscordChannelTests.cs`
- **测试用例**:
  - `StartAsync_ConnectsToGateway`
  - `StopAsync_DisconnectsFromGateway`
  - `SendMessageAsync_SendsToCorrectChannel`
  - `OnMessageReceived_RaisesEvent`
  - `HandlesMentions_Correctly`

#### 3.3 EmailChannel 测试
- **任务**: 实现 EmailChannel 的完整测试
- **文件**: `tests/NanoBot.Channels.Tests/EmailChannelTests.cs`
- **测试用例**:
  - `FetchNewMessages_ParsesUnseenAndMarksSeen`
  - `FetchNewMessages_DeduplicatesByUid`
  - `ExtractTextBody_FallsBackToHtml`
  - `Send_UsesSmtpAndReplySubject`
  - `Start_WithoutConsent_ReturnsImmediately`
- **边界条件**:
  - 附件处理
  - 编码问题
  - 邮件服务器连接失败

#### 3.4 其他通道测试
- **任务**: 实现其他通道的基础测试
- **文件**:
  - `tests/NanoBot.Channels.Tests/FeishuChannelTests.cs`
  - `tests/NanoBot.Channels.Tests/SlackChannelTests.cs`
  - `tests/NanoBot.Channels.Tests/WhatsAppChannelTests.cs`
  - `tests/NanoBot.Channels.Tests/DingTalkChannelTests.cs`
  - `tests/NanoBot.Channels.Tests/QQChannelTests.cs`
  - `tests/NanoBot.Channels.Tests/MochatChannelTests.cs`
- **测试用例**: 每个通道至少包含连接、发送、接收的基础测试

---

### 任务组 4: Infrastructure 层测试增强

#### 4.1 CronService 测试
- **任务**: 实现 CronService 的完整测试
- **文件**: `tests/NanoBot.Infrastructure.Tests/Cron/CronServiceTests.cs`
- **测试用例**:
  - `AddJob_SchedulesCorrectly`
  - `RemoveJob_CancelsScheduledJob`
  - `EnableJob_TogglesJobState`
  - `RunJobAsync_ExecutesImmediately`
  - `ListJobs_ReturnsAllJobs`
  - `Job_ExecutesAtScheduledTime`
  - `Job_PersistsAcrossRestarts`
- **边界条件**:
  - 无效 Cron 表达式
  - 任务执行超时
  - 任务执行异常

#### 4.2 HeartbeatService 测试
- **任务**: 实现 HeartbeatService 的完整测试
- **文件**: `tests/NanoBot.Infrastructure.Tests/Heartbeat/HeartbeatServiceTests.cs`
- **测试用例**:
  - `StartAsync_BeginsSendingHeartbeats`
  - `StopAsync_StopsSendingHeartbeats`
  - `Heartbeat_SendsCorrectInterval`
  - `Heartbeat_IncludesMemoryContent`
- **边界条件**:
  - 内存文件不存在
  - 发送失败重试

#### 4.3 SkillsLoader 测试（已存在，验证完整性）
- **任务**: 验证 SkillsLoader 测试完整性
- **文件**: `tests/NanoBot.Infrastructure.Tests/Skills/SkillsLoaderTests.cs`
- **测试用例**:
  - `LoadSkills_LoadsAllSkills`
  - `LoadSkills_ParsesMetadataCorrectly`
  - `GetSkill_ReturnsSpecificSkill`
  - `GetAlwaysSkills_ReturnsAlwaysLoadSkills`
  - `BuildSkillsSummary_BuildsCorrectSummary`
  - `LoadSkillsForContext_LoadsRelevantSkills`
- **边界条件**:
  - 技能文件格式错误
  - 技能目录不存在
  - 重复技能名称

#### 4.4 SubagentManager 测试
- **任务**: 实现 SubagentManager 的完整测试
- **文件**: `tests/NanoBot.Infrastructure.Tests/Subagents/SubagentManagerTests.cs`
- **测试用例**:
  - `Spawn_CreatesSubagent`
  - `Spawn_WithParentContext_InheritsContext`
  - `List_ReturnsAllSubagents`
  - `Terminate_StopsSubagent`
  - `WaitForCompletion_WaitsForResult`
- **边界条件**:
  - 子 Agent 执行超时
  - 子 Agent 执行异常
  - 并发子 Agent 限制

#### 4.5 MemoryConsolidator 测试
- **任务**: 实现 MemoryConsolidator 的完整测试
- **文件**: `tests/NanoBot.Infrastructure.Tests/Memory/MemoryConsolidatorTests.cs`
- **测试用例**:
  - `Consolidate_ProcessesOldMessages`
  - `Consolidate_UpdatesMemoryFile`
  - `Consolidate_RespectsKeepCount`
  - `ShouldConsolidate_ReturnsTrueWhenThresholdReached`
- **边界条件**:
  - 空历史记录
  - LLM 调用失败

---

### 任务组 5: Agent 层测试增强

#### 5.1 SessionManagerTests Session 核心逻辑测试
- **任务**: 实现 Session 管理器核心逻辑测试
- **文件**: `tests/NanoBot.Agent.Tests/SessionManagerTests.cs`
- **测试用例**:
  - `GetOldMessages_ReturnsCorrectRange`（Theory 测试）
  - `LastConsolidated_PersistsAcrossSaveLoad`
  - `Clear_ResetsLastConsolidatedToZero`
  - `GetHistory_StableForSameMaxMessages`
  - `GetOrCreate_WithNewKey_CreatesNewSession`
  - `GetOrCreate_WithExistingKey_ReturnsCachedSession`
  - `Invalidate_ClearsCache`

#### 5.2 Context Provider 测试
- **任务**: 实现所有 Context Provider 的测试
- **文件**:
  - `tests/NanoBot.Agent.Tests/Context/BootstrapContextProviderTests.cs`
  - `tests/NanoBot.Agent.Tests/Context/MemoryContextProviderTests.cs`
  - `tests/NanoBot.Agent.Tests/Context/SkillsContextProviderTests.cs`
  - `tests/NanoBot.Agent.Tests/Context/FileBackedChatHistoryProviderTests.cs`
- **测试用例**:
  - `ProvideContext_ReturnsCorrectContext`
  - `StoreContext_PersistsChanges`
  - `ProvideContext_WithMissingFile_ReturnsEmpty`
  - `ProvideContext_WithLargeFile_HandlesCorrectly`

#### 5.3 NanoBotAgentFactory 测试
- **任务**: 实现 NanoBotAgentFactory 的完整测试
- **文件**: `tests/NanoBot.Agent.Tests/NanoBotAgentFactoryTests.cs`
- **测试用例**:
  - `Create_ReturnsConfiguredAgent`
  - `Create_WithTools_IncludesTools`
  - `Create_WithContextProviders_IncludesProviders`
  - `Create_BuildsCorrectInstructions`
- **边界条件**:
  - 配置文件缺失
  - 工具列表为空

---

### 任务组 6: 集成测试增强

#### 6.1 Agent 集成测试
- **任务**: 增强 Agent 集成测试
- **文件**: `tests/NanoBot.Integration.Tests/AgentIntegrationTests.cs`
- **新增测试用例**:
  - `Agent_WithToolCall_ExecutesToolAndContinues`
  - `Agent_WithMultipleToolCalls_ExecutesAll`
  - `Agent_WithContextProviders_LoadsContext`
  - `Agent_PersistsHistory_AcrossTurns`
  - `Agent_HandlesCancellation_Gracefully`

#### 6.2 Channel 集成测试
- **任务**: 增强 Channel 集成测试
- **文件**: `tests/NanoBot.Integration.Tests/ChannelIntegrationTests.cs`
- **新增测试用例**:
  - `Channel_ReceivesMessage_RoutesToAgent`
  - `Channel_SendsResponse_Correctly`
  - `MultipleChannels_ProcessIndependently`
  - `Channel_Reconnects_AfterDisconnect`

#### 6.3 端到端测试增强
- **任务**: 增强端到端测试
- **文件**: `tests/NanoBot.Integration.Tests/EndToEndTests.cs`
- **新增测试用例**:
  - `EndToEnd_ToolExecution_WorksCorrectly`
  - `EndToEnd_MemoryPersistence_WorksCorrectly`
  - `EndToEnd_SessionIsolation_WorksCorrectly`
  - `EndToEnd_CronJob_ExecutesCorrectly`

---

### 任务组 7: 测试基础设施改进

#### 7.1 Mock 对象完善
- **任务**: 创建可复用的 Mock 对象
- **文件**: `tests/NanoBot.Tests.Common/Mocks/`
- **内容**:
  - `MockChatClient.cs` - 模拟 LLM 响应
  - `MockMessageBus.cs` - 模拟消息总线
  - `MockWorkspaceManager.cs` - 模拟工作空间
  - `MockChannel.cs` - 模拟通道
  - `MockMemoryStore.cs` - 模拟记忆存储

#### 7.2 测试工具类
- **任务**: 创建测试辅助工具
- **文件**: `tests/NanoBot.Tests.Common/`
- **内容**:
  - `TestDataGenerator.cs` - 测试数据生成
  - `TempDirectory.cs` - 临时目录管理
  - `AssertExtensions.cs` - 自定义断言

#### 7.3 测试配置
- **任务**: 优化测试配置
- **文件**:
  - `tests/xunit.runner.json` - xUnit 配置
  - `tests/Directory.Build.props` - 测试项目共享配置
- **内容**:
  - 并行测试配置
  - 测试超时设置
  - 代码覆盖率配置

---

## 三、测试优先级

### P0 - 必须完成（核心功能）
1. ~~Tools 层功能测试（FileTools, ShellTools）~~ ✅ 已完成
2. CLI 层核心命令测试（Onboard, Agent, Status）
3. ~~Agent 层 Session Manager 核心逻辑测试~~ ✅ 已完成
4. ~~Infrastructure 层核心服务测试（Cron, Heartbeat）~~ ✅ 已完成

### P1 - 应该完成（重要功能）
1. ToolValidationTests 参数验证测试
2. WebTools 功能测试
3. 主要通道测试（Telegram, Discord, Email）
4. Context Provider 测试
5. NanoBotAgentFactory 测试

### P2 - 可以延后（增强覆盖）
1. 其他 CLI 命令测试
2. 其他通道测试
3. SubagentManager 测试
4. MemoryConsolidator 测试
5. 边界条件和异常测试

---

## 四、测试执行计划

### 阶段 1: 核心功能测试（2 周）
- 完成 Tools 层 P0 测试
- 完成 CLI 层 P0 测试
- 完成 Agent 层 Session Manager 核心逻辑测试

### 阶段 2: 服务层测试（2 周）
- 完成 Infrastructure 层 P0 测试
- 完成 P1 测试（ToolValidation, Context Provider）
- 完成主要通道测试
- 完成集成测试增强

### 阶段 3: 完整覆盖（2 周）
- 完成剩余 P1 测试
- 完成 P2 测试
- 完善测试基础设施

---

## 五、测试质量标准

### 5.1 单元测试要求

1. **每个公共方法至少 3 个测试用例**：
   - 正常输入
   - 边界条件
   - 异常/错误情况

2. **使用真实依赖替代部分 Mock**：
   - 文件系统操作使用临时目录
   - HTTP 调用使用 `HttpClient` 模拟服务器

3. **测试命名遵循规范**：
   ```
   [Method]_[Scenario]_[ExpectedResult]
   ```

### 5.2 集成测试要求

1. **使用 TestHost**：
   - 加载完整 DI 容器
   - 测试真实模块交互

2. **使用内存临时存储**：
   - 避免文件系统污染
   - 提高测试执行速度

---

## 六、成功标准

### 覆盖率目标
| 模块 | 当前覆盖率 | 目标覆盖率 |
|------|----------|----------|
| NanoBot.Tools | ~20% | 85% |
| NanoBot.Cli | ~10% | 75% |
| NanoBot.Channels | ~40% | 70% |
| NanoBot.Providers | ~30% | 75% |
| NanoBot.Agent | ~70% | 80% |
| NanoBot.Infrastructure | ~60% | 80% |
| NanoBot.Core | ~80% | 85% |
| **总体** | **~45%** | **75%** |

### 质量标准
- 所有 P0 测试必须通过
- 所有 P1 测试必须通过
- 测试运行时间 < 5 分钟
- 无 flaky 测试
- 代码覆盖率报告生成

---

## 七、依赖与风险

### 依赖
- Phase 4 应用层开发完成
- 所有核心功能实现稳定
- Microsoft.Agents.AI 框架版本稳定

### 风险
| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 外部服务测试不稳定 | 高 | 使用 Mock HTTP 客户端 |
| 测试运行时间过长 | 中 | 优化测试并行度 |
| 测试维护成本高 | 中 | 建立测试最佳实践 |

---

## 相关文档

- [Testing.md](../solutions/Testing.md) - 测试方案设计
- [Phase4-Application.md](./Phase4-Application.md) - 应用层计划
- [Tools.md](../solutions/Tools.md) - 工具层设计
- [Agent-Core.md](../solutions/Agent-Core.md) - Agent 核心设计
- [Infrastructure.md](../solutions/Infrastructure.md) - 基础设施设计
- [Channels.md](../solutions/Channels.md) - 通道层设计
