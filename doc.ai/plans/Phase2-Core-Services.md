# Phase 2: 核心服务层实现计划

本阶段实现 NanoBot.Net 的核心服务层，包括 LLM 提供商、工具系统、通道适配器和基础设施服务。

## 阶段目标

实现完整的 LLM 对接能力、工具执行框架、多通道消息处理和后台服务调度，为 Agent 核心层提供必要的运行时支持。

## 相关方案文档

- [Providers.md](../solutions/Providers.md) - 提供商层设计
- [Tools.md](../solutions/Tools.md) - 工具层设计
- [Channels.md](../solutions/Channels.md) - 通道层设计
- [Infrastructure.md](../solutions/Infrastructure.md) - 基础设施服务设计

## 阶段依赖

- Phase 1 基础设施层已完成
- 配置管理模块可用
- 消息总线模块可用
- Workspace 管理模块可用

## 任务清单概览

| 任务清单 | 主要内容 | 并行度 |
|----------|----------|--------|
| [LLM 提供商模块](#任务清单-llm-提供商模块) | 提供商接口与实现 | 高 |
| [工具系统模块](#任务清单-工具系统模块) | 工具接口与内置工具 | 高 |
| [通道适配器模块](#任务清单-通道适配器模块) | 通道接口与实现 | 高 |
| [后台服务模块](#任务清单-后台服务模块) | Cron、Heartbeat、Skills、Subagent | 高 |

---

## 任务清单：LLM 提供商模块

### 任务目标

实现 LLM 提供商接口和多个主流提供商的实现，支持 OpenAI 兼容 API。

### 相关方案文档

- [Providers.md](../solutions/Providers.md)

### 任务依赖

- 配置管理模块（需要 LlmConfig）

### 任务列表

#### Task 2.1.1: 定义 LLM 核心类型

**描述**: 定义 LLM 请求和响应相关的数据类型。

**交付物**:
- LLMRequest.cs 文件
- LLMResponse.cs 文件
- LLMChunk.cs 文件
- UsageInfo.cs 文件
- ModelInfo.cs 文件

**完成标准**:
- 类型定义与设计文档一致
- 支持流式响应

---

#### Task 2.1.2: 定义工具调用类型

**描述**: 定义工具调用相关的数据类型。

**交付物**:
- ToolDefinition.cs 文件
- FunctionDefinition.cs 文件
- ToolCall.cs 文件
- ToolCallDelta.cs 文件

**完成标准**:
- 类型定义与设计文档一致
- 支持 JSON Schema 参数

---

#### Task 2.1.3: 定义 ILLMProvider 接口

**描述**: 定义 LLM 提供商接口。

**交付物**:
- ILLMProvider.cs 接口文件
- CompleteAsync 方法声明
- StreamCompleteAsync 方法声明

**完成标准**:
- 接口定义与设计文档一致
- 支持同步和流式两种模式

---

#### Task 2.1.4: 定义 IProviderRegistry 接口

**描述**: 定义提供商注册表接口。

**交付物**:
- IProviderRegistry.cs 接口文件
- 注册、获取、创建方法声明

**完成标准**:
- 接口定义与设计文档一致
- 支持默认提供商设置

---

#### Task 2.1.5: 实现 ProviderRegistry 类

**描述**: 实现提供商注册表。

**交付物**:
- ProviderRegistry.cs 实现文件
- 提供商字典管理
- 工厂方法实现

**完成标准**:
- 支持动态注册
- 支持配置创建提供商

---

#### Task 2.1.6: 实现 OpenAI 兼容基类

**描述**: 实现 OpenAI 兼容 API 的基类，供多个提供商复用。

**交付物**:
- OpenAICompatibleProvider.cs 基类
- HTTP 客户端配置
- 请求/响应处理逻辑

**完成标准**:
- 支持标准 OpenAI API 格式
- 支持流式响应解析
- 支持工具调用

---

#### Task 2.1.7: 实现 OpenAI Provider

**描述**: 实现 OpenAI 官方 API 提供商。

**交付物**:
- OpenAIProvider.cs 文件
- 默认模型 gpt-4o

**完成标准**:
- 正确调用 OpenAI API
- 支持所有模型

---

#### Task 2.1.8: 实现 OpenRouter Provider

**描述**: 实现 OpenRouter 聚合提供商。

**交付物**:
- OpenRouterProvider.cs 文件
- 默认模型 anthropic/claude-3.5-sonnet

**完成标准**:
- 正确调用 OpenRouter API
- 支持 100+ 模型

---

#### Task 2.1.9: 实现 Anthropic Provider

**描述**: 实现 Anthropic 官方 API 提供商。

**交付物**:
- AnthropicProvider.cs 文件
- Claude API 格式适配

**完成标准**:
- 正确调用 Anthropic API
- 支持所有 Claude 模型

---

#### Task 2.1.10: 实现 Ollama Provider

**描述**: 实现本地 Ollama 提供商。

**交付物**:
- OllamaProvider.cs 文件
- 本地服务连接

**完成标准**:
- 正确调用 Ollama API
- 支持本地模型

---

#### Task 2.1.11: 实现其他提供商

**描述**: 实现 DeepSeek、Groq、Moonshot、Zhipu 等提供商。

**交付物**:
- DeepSeekProvider.cs
- GroqProvider.cs
- MoonshotProvider.cs
- ZhipuProvider.cs

**完成标准**:
- 各提供商正确实现
- API 格式正确

---

#### Task 2.1.12: 编写提供商模块单元测试

**描述**: 编写提供商模块的单元测试。

**交付物**:
- NanoBot.Core.Providers.Tests 项目
- ProviderRegistryTests.cs
- OpenAIProviderTests.cs

**完成标准**:
- 测试覆盖率 >= 75%
- Mock HTTP 响应测试
- 所有测试通过

### 成功指标

- 至少支持 5 个主流提供商
- API 调用成功率 >= 99%
- 流式响应正常工作
- 单元测试覆盖率 >= 75%

---

## 任务清单：工具系统模块

### 任务目标

实现工具接口、工具注册表和所有内置工具。

### 相关方案文档

- [Tools.md](../solutions/Tools.md)

### 任务依赖

- 配置管理模块（需要 SecurityConfig）
- Workspace 管理模块（需要工作目录）

### 任务列表

#### Task 2.2.1: 定义工具核心类型

**描述**: 定义工具相关的数据类型。

**交付物**:
- ToolResult.cs 文件
- ValidationResult.cs 文件
- IToolContext.cs 接口

**完成标准**:
- 类型定义与设计文档一致
- ValidationResult 包含工厂方法

---

#### Task 2.2.2: 定义 ITool 接口

**描述**: 定义工具接口。

**交付物**:
- ITool.cs 接口文件
- ExecuteAsync 方法声明
- ValidateParameters 方法声明

**完成标准**:
- 接口定义与设计文档一致
- 支持参数验证

---

#### Task 2.2.3: 定义 IToolRegistry 接口

**描述**: 定义工具注册表接口。

**交付物**:
- IToolRegistry.cs 接口文件
- 注册、获取、Schema 生成方法

**完成标准**:
- 接口定义与设计文档一致
- 支持 OpenAI 函数 Schema 生成

---

#### Task 2.2.4: 实现 ToolRegistry 类

**描述**: 实现工具注册表。

**交付物**:
- ToolRegistry.cs 实现文件
- 工具字典管理
- Schema 生成逻辑

**完成标准**:
- 支持动态注册
- 自动生成函数 Schema

---

#### Task 2.2.5: 实现 ToolBase 基类

**描述**: 实现工具基类，提供通用功能。

**交付物**:
- ToolBase.cs 抽象类
- 路径验证逻辑
- 参数解析辅助方法

**完成标准**:
- 提供路径安全检查
- 支持 JSON 参数解析

---

#### Task 2.2.6: 实现 read_file 工具

**描述**: 实现文件读取工具。

**交付物**:
- ReadFileTool.cs 文件
- ReadFileArgs 参数类

**完成标准**:
- 支持行范围读取
- 路径安全检查

---

#### Task 2.2.7: 实现 write_file 工具

**描述**: 实现文件写入工具。

**交付物**:
- WriteFileTool.cs 文件
- WriteFileArgs 参数类

**完成标准**:
- 自动创建父目录
- 支持追加模式

---

#### Task 2.2.8: 实现 edit_file 工具

**描述**: 实现文件编辑工具。

**交付物**:
- EditFileTool.cs 文件
- EditFileArgs 参数类

**完成标准**:
- 支持文本替换
- 支持全部替换

---

#### Task 2.2.9: 实现 list_dir 工具

**描述**: 实现目录列表工具。

**交付物**:
- ListDirTool.cs 文件
- ListDirArgs 参数类

**完成标准**:
- 支持递归列表
- 支持模式过滤

---

#### Task 2.2.10: 实现 exec 工具

**描述**: 实现 Shell 命令执行工具。

**交付物**:
- ExecTool.cs 文件
- ExecArgs 参数类

**完成标准**:
- 支持超时控制
- 支持命令黑名单
- 支持工作目录限制

---

#### Task 2.2.11: 实现 web_search 工具

**描述**: 实现网页搜索工具。

**交付物**:
- WebSearchTool.cs 文件
- WebSearchArgs 参数类

**完成标准**:
- 集成 Brave Search API
- 支持结果数量限制

---

#### Task 2.2.12: 实现 web_fetch 工具

**描述**: 实现网页获取工具。

**交付物**:
- WebFetchTool.cs 文件
- WebFetchArgs 参数类

**完成标准**:
- 支持可读内容提取
- 支持字符数限制

---

#### Task 2.2.13: 实现 message 工具

**描述**: 实现消息发送工具。

**交付物**:
- MessageTool.cs 文件
- MessageArgs 参数类

**完成标准**:
- 支持跨通道发送
- 正确使用消息总线

---

#### Task 2.2.14: 实现 spawn 工具

**描述**: 实现子 Agent 创建工具。

**交付物**:
- SpawnTool.cs 文件
- SpawnArgs 参数类

**完成标准**:
- 正确调用 SubagentManager
- 返回任务 ID

---

#### Task 2.2.15: 实现 cron 工具

**描述**: 实现定时任务管理工具。

**交付物**:
- CronTool.cs 文件
- CronArgs 参数类

**完成标准**:
- 支持添加/删除/启用/禁用任务
- 正确调用 CronService

---

#### Task 2.2.16: 实现 MCP 客户端接口

**描述**: 实现 MCP 客户端接口和基础实现。

**交付物**:
- IMcpClient.cs 接口
- McpClient.cs 实现
- McpTool、McpToolResult 类型

**完成标准**:
- 支持 stdio 通信
- 支持工具发现和调用

---

#### Task 2.2.17: 编写工具模块单元测试

**描述**: 编写工具模块的单元测试。

**交付物**:
- NanoBot.Core.Tools.Tests 项目
- ToolRegistryTests.cs
- ToolValidationTests.cs
- FileToolsTests.cs
- ShellToolTests.cs
- WebToolsTests.cs

**完成标准**:
- 测试覆盖率 >= 85%
- 参数验证测试完整
- 所有测试通过

### 成功指标

- 所有内置工具实现完成
- 参数验证覆盖所有边界条件
- 工具执行安全可控
- 单元测试覆盖率 >= 85%

---

## 任务清单：通道适配器模块

### 任务目标

实现通道接口和所有支持的通道适配器。

### 相关方案文档

- [Channels.md](../solutions/Channels.md)

### 任务依赖

- 配置管理模块（需要通道配置）
- 消息总线模块（需要消息发布）

### 任务列表

#### Task 2.3.1: 定义通道核心类型

**描述**: 定义通道相关的数据类型。

**交付物**:
- InboundMessage.cs 文件（如未在 Phase 1 创建）
- OutboundMessage.cs 文件（如未在 Phase 1 创建）
- ChannelStatus.cs 文件

**完成标准**:
- 类型定义与设计文档一致
- SessionKey 计算正确

---

#### Task 2.3.2: 定义 IChannel 接口

**描述**: 定义通道接口。

**交付物**:
- IChannel.cs 接口文件
- StartAsync、StopAsync 方法声明
- SendMessageAsync 方法声明
- MessageReceived 事件声明

**完成标准**:
- 接口定义与设计文档一致
- 包含 IsConnected 属性

---

#### Task 2.3.3: 定义 IChannelManager 接口

**描述**: 定义通道管理器接口。

**交付物**:
- IChannelManager.cs 接口文件
- 注册、启动、停止方法声明
- 状态查询方法

**完成标准**:
- 接口定义与设计文档一致
- 支持 EnabledChannels 属性

---

#### Task 2.3.4: 实现 ChannelManager 类

**描述**: 实现通道管理器。

**交付物**:
- ChannelManager.cs 实现文件
- 通道字典管理
- 生命周期管理

**完成标准**:
- 支持动态注册
- 正确聚合消息事件

---

#### Task 2.3.5: 实现 Telegram 通道

**描述**: 实现 Telegram Bot API 通道。

**交付物**:
- TelegramChannel.cs 文件
- Long Polling 实现
- Markdown 转 HTML 逻辑

**完成标准**:
- 正确接收和发送消息
- 支持代理
- 支持输入指示

---

#### Task 2.3.6: 实现 Discord 通道

**描述**: 实现 Discord Gateway 通道。

**交付物**:
- DiscordChannel.cs 文件
- WebSocket 连接实现
- Rate Limit 处理

**完成标准**:
- 正确处理 Gateway 心跳
- 正确接收和发送消息
- 支持附件下载

---

#### Task 2.3.7: 实现 Feishu 通道

**描述**: 实现飞书开放平台通道。

**交付物**:
- FeishuChannel.cs 文件
- WebSocket 长连接实现
- 消息去重逻辑

**完成标准**:
- 正确接收和发送消息
- 支持富文本卡片
- 支持反应表情

---

#### Task 2.3.8: 实现 Email 通道

**描述**: 实现 IMAP/SMTP 邮件通道。

**交付物**:
- EmailChannel.cs 文件
- IMAP 轮询实现
- SMTP 发送实现

**完成标准**:
- 正确接收新邮件
- 正确发送回复
- 支持历史查询
- 支持去重

---

#### Task 2.3.9: 实现 Slack 通道

**描述**: 实现 Slack Socket Mode 通道。

**交付物**:
- SlackChannel.cs 文件
- Socket Mode 实现
- 线程回复支持

**完成标准**:
- 正确接收和发送消息
- 支持 app_mention 事件
- 支持反应表情

---

#### Task 2.3.10: 实现其他通道

**描述**: 实现 WhatsApp、DingTalk、QQ、Mochat 通道。

**交付物**:
- WhatsAppChannel.cs（通过 Bridge）
- DingTalkChannel.cs
- QQChannel.cs
- MochatChannel.cs

**完成标准**:
- 各通道正确实现
- 消息收发正常

---

#### Task 2.3.11: 编写通道模块单元测试

**描述**: 编写通道模块的单元测试。

**交付物**:
- NanoBot.Core.Channels.Tests 项目
- ChannelManagerTests.cs
- TelegramChannelTests.cs
- DiscordChannelTests.cs
- EmailChannelTests.cs

**完成标准**:
- 测试覆盖率 >= 70%
- Mock 外部服务测试
- 所有测试通过

### 成功指标

- 至少 3 个通道实现完成
- 消息收发正确
- 连接稳定性高
- 单元测试覆盖率 >= 70%

---

## 任务清单：后台服务模块

### 任务目标

实现 Cron 定时任务、Heartbeat 心跳、Skills 加载和 Subagent 管理等后台服务。

### 相关方案文档

- [Infrastructure.md](../solutions/Infrastructure.md) - ICronService、IHeartbeatService、ISkillsLoader、ISubagentManager

### 任务依赖

- 配置管理模块
- 消息总线模块
- Workspace 管理模块

### 任务列表

#### Task 2.4.1: 定义 Cron 服务类型

**描述**: 定义定时任务相关的数据类型。

**交付物**:
- CronJobDefinition.cs 文件
- CronSchedule.cs 文件
- CronJob.cs 文件
- CronServiceStatus.cs 文件

**完成标准**:
- 类型定义与设计文档一致
- 支持 At、Every、Cron 三种调度

---

#### Task 2.4.2: 定义 ICronService 接口

**描述**: 定义定时任务服务接口。

**交付物**:
- ICronService.cs 接口文件
- 任务管理方法声明

**完成标准**:
- 接口定义与设计文档一致

---

#### Task 2.4.3: 实现 CronService 类

**描述**: 实现定时任务服务。

**交付物**:
- CronService.cs 实现文件
- Cronos 库集成
- 持久化存储

**完成标准**:
- 支持三种调度类型
- 支持任务持久化
- 正确计算下次执行时间

---

#### Task 2.4.4: 定义 Heartbeat 服务类型

**描述**: 定义心跳服务相关的数据类型。

**交付物**:
- HeartbeatDefinition.cs 文件
- HeartbeatJob.cs 文件
- HeartbeatStatus.cs 文件

**完成标准**:
- 类型定义与设计文档一致

---

#### Task 2.4.5: 定义 IHeartbeatService 接口

**描述**: 定义心跳服务接口。

**交付物**:
- IHeartbeatService.cs 接口文件

**完成标准**:
- 接口定义与设计文档一致

---

#### Task 2.4.6: 实现 HeartbeatService 类

**描述**: 实现心跳服务。

**交付物**:
- HeartbeatService.cs 实现文件
- HEARTBEAT.md 文件解析
- 定时触发逻辑

**完成标准**:
- 正确解析心跳任务
- 定时触发 Agent 执行

---

#### Task 2.4.7: 定义 Skills 加载类型

**描述**: 定义 Skills 相关的数据类型。

**交付物**:
- Skill.cs 文件
- SkillMetadata.cs 文件
- SkillSummary.cs 文件
- SkillsChangedEventArgs.cs 文件

**完成标准**:
- 类型定义与设计文档一致
- 支持 YAML frontmatter 解析

---

#### Task 2.4.8: 定义 ISkillsLoader 接口

**描述**: 定义 Skills 加载器接口。

**交付物**:
- ISkillsLoader.cs 接口文件
- 加载、重载、摘要方法声明

**完成标准**:
- 接口定义与设计文档一致
- 支持渐进式加载

---

#### Task 2.4.9: 实现 SkillsLoader 类

**描述**: 实现 Skills 加载器。

**交付物**:
- SkillsLoader.cs 实现文件
- SKILL.md 解析逻辑
- 依赖检查逻辑
- 热重载支持

**完成标准**:
- 正确解析 Skill 元数据
- 检查 bins 和 env 依赖
- 支持 SkillsChanged 事件

---

#### Task 2.4.10: 定义 Subagent 管理类型

**描述**: 定义 Subagent 相关的数据类型。

**交付物**:
- SubagentInfo.cs 文件
- SubagentStatus.cs 枚举
- SubagentResult.cs 文件
- SubagentCompletedEventArgs.cs 文件

**完成标准**:
- 类型定义与设计文档一致

---

#### Task 2.4.11: 定义 ISubagentManager 接口

**描述**: 定义 Subagent 管理器接口。

**交付物**:
- ISubagentManager.cs 接口文件

**完成标准**:
- 接口定义与设计文档一致

---

#### Task 2.4.12: 实现 SubagentManager 类

**描述**: 实现 Subagent 管理器。

**交付物**:
- SubagentManager.cs 实现文件
- 后台任务执行
- 完成通知

**完成标准**:
- 正确创建后台 Agent
- 支持任务取消
- 触发完成事件

---

#### Task 2.4.13: 编写后台服务单元测试

**描述**: 编写后台服务的单元测试。

**交付物**:
- NanoBot.Core.Services.Tests 项目
- CronServiceTests.cs
- HeartbeatServiceTests.cs
- SkillsLoaderTests.cs
- SubagentManagerTests.cs

**完成标准**:
- 测试覆盖率 >= 80%
- 所有测试通过

### 成功指标

- Cron 任务调度准确
- Heartbeat 触发正常
- Skills 加载和依赖检查正确
- Subagent 创建和管理正常
- 单元测试覆盖率 >= 80%

---

## 风险评估

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|----------|
| LLM API 变更 | 高 | 中 | 抽象提供商接口，隔离 API 差异 |
| 通道连接不稳定 | 中 | 中 | 实现重连机制，错误处理 |
| 工具执行安全 | 高 | 低 | 严格路径检查，命令黑名单 |
| 后台任务泄漏 | 中 | 低 | 正确管理资源，实现取消机制 |

## 阶段完成标准

- 所有任务清单完成
- 所有单元测试通过
- 代码审查通过
- 至少 3 个 LLM 提供商可用
- 至少 3 个通道可用
- 所有内置工具可用

## 下一阶段

完成本阶段后，进入 [Phase 3: Agent 核心层](./Phase3-Agent-Core.md)。
