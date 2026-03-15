# Source Code Inventory

生成时间: 2026-03-15 10:40:18

扫描路径: `src/`

## 项目概览

| 项目名称 | 文件数 | 类 | 接口 | 枚举 | Record | 结构体 |
|----------|--------|-----|------|------|--------|--------|
| NanoBot.Agent | 19 | 21 | 4 | 1 | 6 | 0 |
| NanoBot.Channels | 11 | 13 | 0 | 0 | 1 | 0 |
| NanoBot.Cli | 17 | 16 | 1 | 1 | 0 | 0 |
| NanoBot.Core | 88 | 66 | 21 | 4 | 44 | 0 |
| NanoBot.Infrastructure | 21 | 24 | 2 | 0 | 0 | 0 |
| NanoBot.Providers | 6 | 9 | 3 | 0 | 1 | 0 |
| NanoBot.Tools | 14 | 13 | 1 | 0 | 3 | 1 |
| NanoBot.WebUI | 10 | 9 | 3 | 1 | 2 | 0 |

## 详细清单

### NanoBot.Agent

**项目文件:** `src/NanoBot.Agent/NanoBot.Agent.csproj`

#### (根目录)/

**AgentRuntime.cs**
- `class AgentRuntime`, `interface IAgentRuntime`, `record CommandDefinition`

**BusProgressReporter.cs**
- `class BusProgressReporter`

**IProgressReporter.cs**
- `interface IProgressReporter`

**NanoBotAgentFactory.cs**
- `class AgentOptions`, `class CompositeAIContextProvider`, `class NanoBotAgentFactory`, `class will`

**SessionManager.cs**
- `class SessionManager`, `interface ISessionManager`, `record JsonlSessionMessage`, `record SessionFileInfo`, `record SessionImageMetadata`

**ToolHintFormatter.cs**
- `class ToolHintFormatter`

#### Context/

**BootstrapContextProvider.cs**
- `class BootstrapContextProvider`

**CompositeChatHistoryProvider.cs**
- `class CompositeChatHistoryProvider`

**FileBackedChatHistoryProvider.cs**
- `class FileBackedChatHistoryProvider`

**MemoryConsolidationChatHistoryProvider.cs**
- `class MemoryConsolidationChatHistoryProvider`

**MemoryConsolidationContextProvider.cs**
- `class MemoryConsolidationContextProvider`

**MemoryContextProvider.cs**
- `class MemoryContextProvider`

**SkillsContextProvider.cs**
- `class SkillsContextProvider`

#### Extensions/

**AgentSessionExtensions.cs**
- `class AgentSessionExtensions`

**ServiceCollectionExtensions.cs**
- `class ServiceCollectionExtensions`

#### Messages/

**MessageAdapter.cs**
- `class MessageAdapter`

**MessagePartUpdate.cs**
- `class MessageStreamOptions`, `enum UpdateType`, `record MessagePartUpdate`, `record ToolExecutionResult`

#### Tools/

**SpawnTool.cs**
- `class SpawnTool`

**ToolExecutionTracker.cs**
- `class ToolExecutionTracker`, `interface IToolExecutionTracker`


### NanoBot.Channels

**项目文件:** `src/NanoBot.Channels/NanoBot.Channels.csproj`

#### (根目录)/

**ChannelManager.cs**
- `class ChannelManager`

#### Abstractions/

**ChannelBase.cs**
- `class ChannelBase`

#### Implementations/DingTalk/

**DingTalkChannel.cs**
- `class DingTalkChannel`

#### Implementations/Discord/

**DiscordChannel.cs**
- `class DiscordChannel`

#### Implementations/Email/

**EmailChannel.cs**
- `class EmailChannel`, `record EmailMessage`

#### Implementations/Feishu/

**FeishuChannel.cs**
- `class FeishuChannel`

#### Implementations/Mochat/

**MochatChannel.cs**
- `class MochatChannel`

#### Implementations/QQ/

**QQChannel.cs**
- `class QQChannel`

#### Implementations/Slack/

**SlackChannel.cs**
- `class SlackChannel`

#### Implementations/Telegram/

**TelegramChannel.cs**
- `class MediaGroupBuffer`, `class TelegramChannel`

#### Implementations/WhatsApp/

**WhatsAppChannel.cs**
- `class WhatsAppChannel`, `class WhatsAppStatusEventArgs`


### NanoBot.Cli

**项目文件:** `src/NanoBot.Cli/NanoBot.Cli.csproj`

#### (根目录)/

**Program.cs**
- `class Program`

#### Commands/

**AgentCommand.cs**
- `class AgentCommand`

**BenchmarkCommand.cs**
- `class BenchmarkCommand`

**ChannelsCommand.cs**
- `class ChannelsCommand`

**ConfigCommand.cs**
- `class ConfigCommand`

**CronCommand.cs**
- `class CronCommand`

**GatewayCommand.cs**
- `class GatewayCommand`

**ICliCommand.cs**
- `interface ICliCommand`

**McpCommand.cs**
- `class McpCommand`

**NanoBotCommandBase.cs**
- `class NanoBotCommandBase`

**OnboardCommand.cs**
- `class OnboardCommand`

**ProviderCommand.cs**
- `class ProviderCommand`

**SessionCommand.cs**
- `class SessionCommand`

**StatusCommand.cs**
- `class StatusCommand`

**WebUICommand.cs**
- `class WebUICommand`, `enum WebUISource`

#### Extensions/

**ServiceCollectionExtensions.cs**
- `class ServiceCollectionExtensions`

#### Services/

**LlmProfileConfigService.cs**
- `class LlmProfileConfigService`


### NanoBot.Core

**项目文件:** `src/NanoBot.Core/NanoBot.Core.csproj`

#### Benchmark/

**BenchmarkCase.cs**
- `class BenchmarkCase`

**BenchmarkResult.cs**
- `class BenchmarkResult`, `class CaseResult`, `class ModelCapabilities`

**IBenchmarkEngine.cs**
- `interface IBenchmarkEngine`

**IQuestionBankLoader.cs**
- `interface IQuestionBankLoader`

#### Bus/

**BusMessage.cs**
- `record BusMessage`

**BusMessageType.cs**
- `enum BusMessageType`

**IMessageBus.cs**
- `interface IMessageBus`

**InboundMessage.cs**
- `record InboundMessage`

**OutboundMessage.cs**
- `record OutboundMessage`

#### Channels/

**ChannelMeta.cs**
- `class ChannelMeta`, `class ChannelUiCatalog`, `class ChannelUiMetaEntry`

**ChannelRegistry.cs**
- `class ChannelRegistry`, `interface IChannelRegistry`

**ChannelStatus.cs**
- `record ChannelStatus`

**IChannel.cs**
- `interface IChannel`

**IChannelManager.cs**
- `interface IChannelManager`

#### Configuration/

**ConfigurationChecker.cs**
- `class ConfigurationCheckResult`, `class ConfigurationChecker`

#### Configuration/Extensions/

**ConfigurationLoader.cs**
- `class ConfigurationLoader`

#### Configuration/Models/

**AgentConfig.cs**
- `class AgentConfig`, `class WebToolsConfig`

**BrowserToolsConfig.cs**
- `class BrowserToolsConfig`

**ChannelsConfig.cs**
- `class ChannelsConfig`, `class MatrixConfig`

**FileToolsConfig.cs**
- `class FileEditConfig`, `class FileReadConfig`, `class FileToolsConfig`

**HeartbeatConfig.cs**
- `class HeartbeatConfig`

**LlmConfig.cs**
- `class LlmConfig`, `class LlmProfile`

**McpConfig.cs**
- `class McpConfig`

**McpServerConfig.cs**
- `class McpServerConfig`

**MemoryConfig.cs**
- `class MemoryConfig`

**SecurityConfig.cs**
- `class SecurityConfig`

**WebUIConfig.cs**
- `class WebUIAuthConfig`, `class WebUIConfig`, `class WebUICorsConfig`, `class WebUIFeaturesConfig`, `class WebUILocalizationConfig`, `class WebUISecurityConfig`, `class WebUIServerConfig`

**WorkspaceConfig.cs**
- `class WorkspaceConfig`

#### Configuration/Models/Channels/

**DingTalkConfig.cs**
- `class DingTalkConfig`

**DiscordConfig.cs**
- `class DiscordConfig`

**EmailConfig.cs**
- `class EmailConfig`

**FeishuConfig.cs**
- `class FeishuConfig`

**MochatConfig.cs**
- `class MochatConfig`, `class MochatMentionConfig`

**QQConfig.cs**
- `class QQConfig`

**SlackConfig.cs**
- `class SlackConfig`, `class SlackDmConfig`

**TelegramConfig.cs**
- `class TelegramConfig`

**WhatsAppConfig.cs**
- `class WhatsAppConfig`

#### Configuration/Validators/

**ConfigurationValidator.cs**
- `class ConfigurationValidator`, `record ValidationResult`

**WebUIConfigValidator.cs**
- `class ValidationResult`, `class WebUIConfigValidator`

#### Constants/

**AgentConstants.cs**
- `class Bootstrap`, `class Channels`, `class Commands`, `class EnvironmentVariables`

#### Cron/

**CronJob.cs**
- `record CronJob`

**CronJobDefinition.cs**
- `record CronJobDefinition`

**CronJobState.cs**
- `record CronJobState`

**CronSchedule.cs**
- `enum CronScheduleKind`, `record CronSchedule`

**CronServiceStatus.cs**
- `record CronServiceStatus`

**ICronService.cs**
- `class CronJobEventArgs`, `interface ICronService`

#### Heartbeat/

**HeartbeatDefinition.cs**
- `record HeartbeatDefinition`

**HeartbeatJob.cs**
- `record HeartbeatJob`

**HeartbeatStatus.cs**
- `record HeartbeatStatus`

**IHeartbeatService.cs**
- `class HeartbeatEventArgs`, `interface IHeartbeatService`

#### Memory/

**IMemoryStore.cs**
- `interface IMemoryStore`

#### Messages/

**FilePart.cs**
- `record FilePart`

**MessageMetadata.cs**
- `record CacheTokenUsage`, `record CostInfo`, `record ErrorInfo`, `record MessageMetadata`, `record ModelInfo`, `record TokenUsage`

**MessagePart.cs**
- `record MessagePart`

**MessageWithParts.cs**
- `record MessageWithParts`

**ReasoningPart.cs**
- `enum ReasoningType`, `record ReasoningPart`

**TextPart.cs**
- `record TextPart`, `record TimeRange`

**ToolPart.cs**
- `record ToolPart`

**ToolStates.cs**
- `record CompletedToolState`, `record ErrorToolState`, `record FileAttachment`, `record PendingToolState`, `record RunningToolState`, `record ToolState`

#### Services/

**ChannelConfigService.cs**
- `class ChannelConfigService`, `interface IChannelConfigService`

**ICostCalculator.cs**
- `interface ICostCalculator`, `record ModelPricing`

#### Sessions/

**AttachmentInfo.cs**
- `class AttachmentInfo`

**IAgentService.cs**
- `interface IAgentService`, `record AgentResponseChunk`, `record ToolCallInfo`

**ISessionService.cs**
- `interface ISessionService`

**MessageInfo.cs**
- `class MessageInfo`, `class ToolExecutionInfo`

**SessionInfo.cs**
- `class SessionInfo`

#### Skills/

**ISkillsLoader.cs**
- `interface ISkillsLoader`

**Skill.cs**
- `record Skill`

**SkillMetadata.cs**
- `record InstallMetadata`, `record NanobotMetadata`, `record RequirementsMetadata`, `record SkillMetadata`

**SkillSummary.cs**
- `record SkillSummary`

**SkillsChangedEventArgs.cs**
- `class SkillsChangedEventArgs`

#### Storage/

**IFileStorageService.cs**
- `interface IFileStorageService`

#### Subagents/

**ISubagentManager.cs**
- `interface ISubagentManager`

**SubagentCompletedEventArgs.cs**
- `class SubagentCompletedEventArgs`

**SubagentInfo.cs**
- `record SubagentInfo`

**SubagentResult.cs**
- `record SubagentResult`

**SubagentStatus.cs**
- `enum SubagentStatus`

#### Tools/

**ToolExecutionContext.cs**
- `class ToolExecutionContext`

#### Tools/Browser/

**BrowserActionRequest.cs**
- `class BrowserActionRequest`

**BrowserTabInfo.cs**
- `class BrowserTabInfo`

**BrowserToolRequest.cs**
- `class BrowserToolRequest`

**BrowserToolResponse.cs**
- `class BrowserToolResponse`

**IBrowserService.cs**
- `interface IBrowserService`

**IPlaywrightInstaller.cs**
- `interface IPlaywrightInstaller`

**IPowerShellInstaller.cs**
- `interface IPowerShellInstaller`

#### Workspace/

**BootstrapFile.cs**
- `record BootstrapFile`

**IBootstrapLoader.cs**
- `interface IBootstrapLoader`

**IWorkspaceManager.cs**
- `interface IWorkspaceManager`


### NanoBot.Infrastructure

**项目文件:** `src/NanoBot.Infrastructure/NanoBot.Infrastructure.csproj`

#### Browser/

**BrowserRefSnapshot.cs**
- `class BrowserRefSnapshot`

**BrowserService.cs**
- `class BrowserService`, `class ProfileState`, `class SnapshotNode`

**IPlaywrightSessionManager.cs**
- `interface IPlaywrightSessionManager`

**PlaywrightInstaller.cs**
- `class PlaywrightInstaller`

**PlaywrightSessionManager.cs**
- `class PlaywrightSessionManager`, `class ProfileState`

**PowerShellInstaller.cs**
- `class PowerShellInstaller`

#### Bus/

**MessageBus.cs**
- `class MessageBus`

#### Cron/

**CronService.cs**
- `class CronService`

#### Extensions/

**ServiceCollectionExtensions.cs**
- `class ServiceCollectionExtensions`

#### Heartbeat/

**HeartbeatService.cs**
- `class HeartbeatService`

#### Memory/

**MemoryConsolidator.cs**
- `class MemoryConsolidator`

**MemoryStore.cs**
- `class MemoryStore`

#### Resources/

**EmbeddedResourceLoader.cs**
- `class EmbeddedResourceLoader`

**IEmbeddedResourceLoader.cs**
- `interface IEmbeddedResourceLoader`

#### Serialization/

**MessagePartJsonConverter.cs**
- `class JsonSerializationExtensions`, `class MessagePartJsonConverter`, `class ToolStateJsonConverter`

#### Services/

**CostCalculator.cs**
- `class CostCalculator`

#### Skills/

**SkillsLoader.cs**
- `class SkillsLoader`

#### Storage/

**FileStorageService.cs**
- `class FileStorageService`

#### Subagents/

**SubagentManager.cs**
- `class SubagentManager`

#### Workspace/

**BootstrapLoader.cs**
- `class BootstrapLoader`

**WorkspaceManager.cs**
- `class WorkspaceManager`


### NanoBot.Providers

**项目文件:** `src/NanoBot.Providers/NanoBot.Providers.csproj`

#### (根目录)/

**ChatClientFactory.cs**
- `class ChatClientFactory`, `interface IChatClientFactory`, `record ProviderSpec`

**InterimTextRetryChatClient.cs**
- `class InterimTextRetryChatClient`, `interface IInterimTextRetryChatClient`

**SanitizingChatClient.cs**
- `class MessageSanitizer`, `class SanitizingChatClient`, `interface ISanitizingChatClient`

#### Benchmark/

**BenchmarkEngine.cs**
- `class BenchmarkEngine`

#### Decorators/

**TokenCountingChatClient.cs**
- `class TokenCountingChatClient`, `class TokenUsageContext`, `class TokenUsageHolder`

#### Extensions/

**ServiceCollectionExtensions.cs**
- `class ServiceCollectionExtensions`


### NanoBot.Tools

**项目文件:** `src/NanoBot.Tools/NanoBot.Tools.csproj`

#### (根目录)/

**ToolProvider.cs**
- `class ToolProvider`

#### BuiltIn/Browser/

**BrowserTools.cs**
- `class BrowserTools`

#### BuiltIn/Cron/

**CronTools.cs**
- `class CronTools`

#### BuiltIn/Filesystem/

**FileTools.cs**
- `class FileTools`

#### BuiltIn/Filesystem/Enhanced/

**EnhancedFileReader.cs**
- `class EnhancedFileReader`

#### BuiltIn/Filesystem/Enhanced/Models/

**MatchResult.cs**
- `record MatchResult`, `struct MatchResult`

**ReadResult.cs**
- `record ReadResult`

#### BuiltIn/Message/

**MessageTools.cs**
- `class MessageTools`

#### BuiltIn/Shell/

**ShellTools.cs**
- `class ShellToolOptions`, `class ShellTools`

#### BuiltIn/Spawn/

**SpawnTools.cs**
- `class SpawnTools`

#### BuiltIn/Web/

**WebTools.cs**
- `class WebTools`

#### Extensions/

**ServiceCollectionExtensions.cs**
- `class ServiceCollectionExtensions`

#### Mcp/

**IMcpClient.cs**
- `interface IMcpClient`, `record McpServerConfig`

**McpClient.cs**
- `class McpClientWrapper`, `class NanoBotMcpClient`


### NanoBot.WebUI

**项目文件:** `src/NanoBot.WebUI/NanoBot.WebUI.csproj`

#### Controllers/

**FilesController.cs**
- `class FilesController`

#### Hubs/

**ChatHub.cs**
- `class ChatHub`

#### Middleware/

**UserFriendlyExceptionMiddleware.cs**
- `class UserFriendlyExceptionMiddleware`, `class UserFriendlyExceptionMiddlewareExtensions`, `enum ErrorType`, `record ErrorInfo`

#### Services/

**AgentService.cs**
- `class AgentService`

**AuthService.cs**
- `class AuthService`

**IAuthService.cs**
- `interface IAuthService`

**IWebUIConfigService.cs**
- `interface IWebUIConfigService`

**LocalizationService.cs**
- `class LocalizationService`, `interface ILocalizationService`

**SessionService.cs**
- `class SessionService`, `record SessionImageItem`

**WebUIConfigService.cs**
- `class WebUIConfigService`


---

## 统计汇总

| 指标 | 数量 |
|------|------|
| 项目数 | 8 |
| 文件数 | 186 |
| 类 (Class) | 171 |
| 接口 (Interface) | 35 |
| 枚举 (Enum) | 7 |
| Record | 57 |
| 结构体 (Struct) | 1 |
| **类型总计** | **271** |

### 按命名空间分组

#### NanoBot.Agent

- `class AgentOptions` (NanoBot.Agent)
- `class AgentRuntime` (NanoBot.Agent)
- `class BusProgressReporter` (NanoBot.Agent)
- `record CommandDefinition` (NanoBot.Agent)
- `class CompositeAIContextProvider` (NanoBot.Agent)
- `interface IAgentRuntime` (NanoBot.Agent)
- `interface IProgressReporter` (NanoBot.Agent)
- `interface ISessionManager` (NanoBot.Agent)
- `record JsonlSessionMessage` (NanoBot.Agent)
- `class NanoBotAgentFactory` (NanoBot.Agent)
- `class ServiceCollectionExtensions` (NanoBot.Agent)
- `record SessionFileInfo` (NanoBot.Agent)
- `record SessionImageMetadata` (NanoBot.Agent)
- `class SessionManager` (NanoBot.Agent)
- `class ToolHintFormatter` (NanoBot.Agent)
- `class will` (NanoBot.Agent)

#### NanoBot.Agent.Context

- `class BootstrapContextProvider` (NanoBot.Agent)
- `class CompositeChatHistoryProvider` (NanoBot.Agent)
- `class FileBackedChatHistoryProvider` (NanoBot.Agent)
- `class MemoryConsolidationChatHistoryProvider` (NanoBot.Agent)
- `class MemoryConsolidationContextProvider` (NanoBot.Agent)
- `class MemoryContextProvider` (NanoBot.Agent)
- `class SkillsContextProvider` (NanoBot.Agent)

#### NanoBot.Agent.Extensions

- `class AgentSessionExtensions` (NanoBot.Agent)

#### NanoBot.Agent.Messages

- `class MessageAdapter` (NanoBot.Agent)
- `record MessagePartUpdate` (NanoBot.Agent)
- `class MessageStreamOptions` (NanoBot.Agent)
- `record ToolExecutionResult` (NanoBot.Agent)
- `enum UpdateType` (NanoBot.Agent)

#### NanoBot.Agent.Tools

- `interface IToolExecutionTracker` (NanoBot.Agent)
- `class SpawnTool` (NanoBot.Agent)
- `class ToolExecutionTracker` (NanoBot.Agent)

#### NanoBot.Channels

- `class ChannelManager` (NanoBot.Channels)

#### NanoBot.Channels.Abstractions

- `class ChannelBase` (NanoBot.Channels)

#### NanoBot.Channels.Implementations.DingTalk

- `class DingTalkChannel` (NanoBot.Channels)

#### NanoBot.Channels.Implementations.Discord

- `class DiscordChannel` (NanoBot.Channels)

#### NanoBot.Channels.Implementations.Email

- `class EmailChannel` (NanoBot.Channels)
- `record EmailMessage` (NanoBot.Channels)

#### NanoBot.Channels.Implementations.Feishu

- `class FeishuChannel` (NanoBot.Channels)

#### NanoBot.Channels.Implementations.Mochat

- `class MochatChannel` (NanoBot.Channels)

#### NanoBot.Channels.Implementations.QQ

- `class QQChannel` (NanoBot.Channels)

#### NanoBot.Channels.Implementations.Slack

- `class SlackChannel` (NanoBot.Channels)

#### NanoBot.Channels.Implementations.Telegram

- `class MediaGroupBuffer` (NanoBot.Channels)
- `class TelegramChannel` (NanoBot.Channels)

#### NanoBot.Channels.Implementations.WhatsApp

- `class WhatsAppChannel` (NanoBot.Channels)
- `class WhatsAppStatusEventArgs` (NanoBot.Channels)

#### NanoBot.Cli

- `class Program` (NanoBot.Cli)

#### NanoBot.Cli.Commands

- `class AgentCommand` (NanoBot.Cli)
- `class BenchmarkCommand` (NanoBot.Cli)
- `class ChannelsCommand` (NanoBot.Cli)
- `class ConfigCommand` (NanoBot.Cli)
- `class CronCommand` (NanoBot.Cli)
- `class GatewayCommand` (NanoBot.Cli)
- `interface ICliCommand` (NanoBot.Cli)
- `class McpCommand` (NanoBot.Cli)
- `class NanoBotCommandBase` (NanoBot.Cli)
- `class OnboardCommand` (NanoBot.Cli)
- `class ProviderCommand` (NanoBot.Cli)
- `class SessionCommand` (NanoBot.Cli)
- `class StatusCommand` (NanoBot.Cli)
- `class WebUICommand` (NanoBot.Cli)
- `enum WebUISource` (NanoBot.Cli)

#### NanoBot.Cli.Extensions

- `class ServiceCollectionExtensions` (NanoBot.Cli)

#### NanoBot.Cli.Services

- `class LlmProfileConfigService` (NanoBot.Cli)

#### NanoBot.Core.Benchmark

- `class BenchmarkCase` (NanoBot.Core)
- `class BenchmarkResult` (NanoBot.Core)
- `class CaseResult` (NanoBot.Core)
- `interface IBenchmarkEngine` (NanoBot.Core)
- `interface IQuestionBankLoader` (NanoBot.Core)
- `class ModelCapabilities` (NanoBot.Core)

#### NanoBot.Core.Bus

- `record BusMessage` (NanoBot.Core)
- `enum BusMessageType` (NanoBot.Core)
- `interface IMessageBus` (NanoBot.Core)
- `record InboundMessage` (NanoBot.Core)
- `record OutboundMessage` (NanoBot.Core)

#### NanoBot.Core.Channels

- `class ChannelMeta` (NanoBot.Core)
- `class ChannelRegistry` (NanoBot.Core)
- `record ChannelStatus` (NanoBot.Core)
- `class ChannelUiCatalog` (NanoBot.Core)
- `class ChannelUiMetaEntry` (NanoBot.Core)
- `interface IChannel` (NanoBot.Core)
- `interface IChannelManager` (NanoBot.Core)
- `interface IChannelRegistry` (NanoBot.Core)

#### NanoBot.Core.Configuration

- `class AgentConfig` (NanoBot.Core)
- `class BrowserToolsConfig` (NanoBot.Core)
- `class ChannelsConfig` (NanoBot.Core)
- `class ConfigurationCheckResult` (NanoBot.Core)
- `class ConfigurationChecker` (NanoBot.Core)
- `class ConfigurationLoader` (NanoBot.Core)
- `class ConfigurationValidator` (NanoBot.Core)
- `class DingTalkConfig` (NanoBot.Core)
- `class DiscordConfig` (NanoBot.Core)
- `class EmailConfig` (NanoBot.Core)
- `class FeishuConfig` (NanoBot.Core)
- `class FileEditConfig` (NanoBot.Core)
- `class FileReadConfig` (NanoBot.Core)
- `class FileToolsConfig` (NanoBot.Core)
- `class HeartbeatConfig` (NanoBot.Core)
- `class LlmConfig` (NanoBot.Core)
- `class LlmProfile` (NanoBot.Core)
- `class MatrixConfig` (NanoBot.Core)
- `class McpConfig` (NanoBot.Core)
- `class McpServerConfig` (NanoBot.Core)
- `class MemoryConfig` (NanoBot.Core)
- `class MochatConfig` (NanoBot.Core)
- `class MochatMentionConfig` (NanoBot.Core)
- `class QQConfig` (NanoBot.Core)
- `class SecurityConfig` (NanoBot.Core)
- `class SlackConfig` (NanoBot.Core)
- `class SlackDmConfig` (NanoBot.Core)
- `class TelegramConfig` (NanoBot.Core)
- `record ValidationResult` (NanoBot.Core)
- `class WebToolsConfig` (NanoBot.Core)
- `class WebUIAuthConfig` (NanoBot.Core)
- `class WebUIConfig` (NanoBot.Core)
- `class WebUICorsConfig` (NanoBot.Core)
- `class WebUIFeaturesConfig` (NanoBot.Core)
- `class WebUILocalizationConfig` (NanoBot.Core)
- `class WebUISecurityConfig` (NanoBot.Core)
- `class WebUIServerConfig` (NanoBot.Core)
- `class WhatsAppConfig` (NanoBot.Core)
- `class WorkspaceConfig` (NanoBot.Core)

#### NanoBot.Core.Configuration.Validators

- `class ValidationResult` (NanoBot.Core)
- `class WebUIConfigValidator` (NanoBot.Core)

#### NanoBot.Core.Constants

- `class Bootstrap` (NanoBot.Core)
- `class Channels` (NanoBot.Core)
- `class Commands` (NanoBot.Core)
- `class EnvironmentVariables` (NanoBot.Core)

#### NanoBot.Core.Cron

- `record CronJob` (NanoBot.Core)
- `record CronJobDefinition` (NanoBot.Core)
- `class CronJobEventArgs` (NanoBot.Core)
- `record CronJobState` (NanoBot.Core)
- `record CronSchedule` (NanoBot.Core)
- `enum CronScheduleKind` (NanoBot.Core)
- `record CronServiceStatus` (NanoBot.Core)
- `interface ICronService` (NanoBot.Core)

#### NanoBot.Core.Heartbeat

- `record HeartbeatDefinition` (NanoBot.Core)
- `class HeartbeatEventArgs` (NanoBot.Core)
- `record HeartbeatJob` (NanoBot.Core)
- `record HeartbeatStatus` (NanoBot.Core)
- `interface IHeartbeatService` (NanoBot.Core)

#### NanoBot.Core.Memory

- `interface IMemoryStore` (NanoBot.Core)

#### NanoBot.Core.Messages

- `record CacheTokenUsage` (NanoBot.Core)
- `record CompletedToolState` (NanoBot.Core)
- `record CostInfo` (NanoBot.Core)
- `record ErrorInfo` (NanoBot.Core)
- `record ErrorToolState` (NanoBot.Core)
- `record FileAttachment` (NanoBot.Core)
- `record FilePart` (NanoBot.Core)
- `record MessageMetadata` (NanoBot.Core)
- `record MessagePart` (NanoBot.Core)
- `record MessageWithParts` (NanoBot.Core)
- `record ModelInfo` (NanoBot.Core)
- `record PendingToolState` (NanoBot.Core)
- `record ReasoningPart` (NanoBot.Core)
- `enum ReasoningType` (NanoBot.Core)
- `record RunningToolState` (NanoBot.Core)
- `record TextPart` (NanoBot.Core)
- `record TimeRange` (NanoBot.Core)
- `record TokenUsage` (NanoBot.Core)
- `record ToolPart` (NanoBot.Core)
- `record ToolState` (NanoBot.Core)

#### NanoBot.Core.Services

- `class ChannelConfigService` (NanoBot.Core)
- `interface IChannelConfigService` (NanoBot.Core)
- `interface ICostCalculator` (NanoBot.Core)
- `record ModelPricing` (NanoBot.Core)

#### NanoBot.Core.Sessions

- `record AgentResponseChunk` (NanoBot.Core)
- `class AttachmentInfo` (NanoBot.Core)
- `interface IAgentService` (NanoBot.Core)
- `interface ISessionService` (NanoBot.Core)
- `class MessageInfo` (NanoBot.Core)
- `class SessionInfo` (NanoBot.Core)
- `record ToolCallInfo` (NanoBot.Core)
- `class ToolExecutionInfo` (NanoBot.Core)

#### NanoBot.Core.Skills

- `interface ISkillsLoader` (NanoBot.Core)
- `record InstallMetadata` (NanoBot.Core)
- `record NanobotMetadata` (NanoBot.Core)
- `record RequirementsMetadata` (NanoBot.Core)
- `record Skill` (NanoBot.Core)
- `record SkillMetadata` (NanoBot.Core)
- `record SkillSummary` (NanoBot.Core)
- `class SkillsChangedEventArgs` (NanoBot.Core)

#### NanoBot.Core.Storage

- `interface IFileStorageService` (NanoBot.Core)

#### NanoBot.Core.Subagents

- `interface ISubagentManager` (NanoBot.Core)
- `class SubagentCompletedEventArgs` (NanoBot.Core)
- `record SubagentInfo` (NanoBot.Core)
- `record SubagentResult` (NanoBot.Core)
- `enum SubagentStatus` (NanoBot.Core)

#### NanoBot.Core.Tools

- `class ToolExecutionContext` (NanoBot.Core)

#### NanoBot.Core.Tools.Browser

- `class BrowserActionRequest` (NanoBot.Core)
- `class BrowserTabInfo` (NanoBot.Core)
- `class BrowserToolRequest` (NanoBot.Core)
- `class BrowserToolResponse` (NanoBot.Core)
- `interface IBrowserService` (NanoBot.Core)
- `interface IPlaywrightInstaller` (NanoBot.Core)
- `interface IPowerShellInstaller` (NanoBot.Core)

#### NanoBot.Core.Workspace

- `record BootstrapFile` (NanoBot.Core)
- `interface IBootstrapLoader` (NanoBot.Core)
- `interface IWorkspaceManager` (NanoBot.Core)

#### NanoBot.Infrastructure.Browser

- `class BrowserRefSnapshot` (NanoBot.Infrastructure)
- `class BrowserService` (NanoBot.Infrastructure)
- `interface IPlaywrightSessionManager` (NanoBot.Infrastructure)
- `class PlaywrightInstaller` (NanoBot.Infrastructure)
- `class PlaywrightSessionManager` (NanoBot.Infrastructure)
- `class PowerShellInstaller` (NanoBot.Infrastructure)
- `class ProfileState` (NanoBot.Infrastructure)
- `class ProfileState` (NanoBot.Infrastructure)
- `class SnapshotNode` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Bus

- `class MessageBus` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Cron

- `class CronService` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Extensions

- `class ServiceCollectionExtensions` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Heartbeat

- `class HeartbeatService` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Memory

- `class MemoryConsolidator` (NanoBot.Infrastructure)
- `class MemoryStore` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Resources

- `class EmbeddedResourceLoader` (NanoBot.Infrastructure)
- `interface IEmbeddedResourceLoader` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Serialization

- `class JsonSerializationExtensions` (NanoBot.Infrastructure)
- `class MessagePartJsonConverter` (NanoBot.Infrastructure)
- `class ToolStateJsonConverter` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Services

- `class CostCalculator` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Skills

- `class SkillsLoader` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Storage

- `class FileStorageService` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Subagents

- `class SubagentManager` (NanoBot.Infrastructure)

#### NanoBot.Infrastructure.Workspace

- `class BootstrapLoader` (NanoBot.Infrastructure)
- `class WorkspaceManager` (NanoBot.Infrastructure)

#### NanoBot.Providers

- `class ChatClientFactory` (NanoBot.Providers)
- `interface IChatClientFactory` (NanoBot.Providers)
- `interface IInterimTextRetryChatClient` (NanoBot.Providers)
- `interface ISanitizingChatClient` (NanoBot.Providers)
- `class InterimTextRetryChatClient` (NanoBot.Providers)
- `class MessageSanitizer` (NanoBot.Providers)
- `record ProviderSpec` (NanoBot.Providers)
- `class SanitizingChatClient` (NanoBot.Providers)

#### NanoBot.Providers.Benchmark

- `class BenchmarkEngine` (NanoBot.Providers)

#### NanoBot.Providers.Decorators

- `class TokenCountingChatClient` (NanoBot.Providers)
- `class TokenUsageContext` (NanoBot.Providers)
- `class TokenUsageHolder` (NanoBot.Providers)

#### NanoBot.Providers.Extensions

- `class ServiceCollectionExtensions` (NanoBot.Providers)

#### NanoBot.Tools

- `class ToolProvider` (NanoBot.Tools)

#### NanoBot.Tools.BuiltIn

- `class BrowserTools` (NanoBot.Tools)
- `class CronTools` (NanoBot.Tools)
- `class FileTools` (NanoBot.Tools)
- `class MessageTools` (NanoBot.Tools)
- `class ShellToolOptions` (NanoBot.Tools)
- `class ShellTools` (NanoBot.Tools)
- `class SpawnTools` (NanoBot.Tools)
- `class WebTools` (NanoBot.Tools)

#### NanoBot.Tools.BuiltIn.Filesystem.Enhanced

- `class EnhancedFileReader` (NanoBot.Tools)

#### NanoBot.Tools.BuiltIn.Filesystem.Enhanced.Models

- `record MatchResult` (NanoBot.Tools)
- `struct MatchResult` (NanoBot.Tools)
- `record ReadResult` (NanoBot.Tools)

#### NanoBot.Tools.Extensions

- `class ServiceCollectionExtensions` (NanoBot.Tools)

#### NanoBot.Tools.Mcp

- `interface IMcpClient` (NanoBot.Tools)
- `class McpClientWrapper` (NanoBot.Tools)
- `record McpServerConfig` (NanoBot.Tools)
- `class NanoBotMcpClient` (NanoBot.Tools)

#### NanoBot.WebUI.Controllers

- `class FilesController` (NanoBot.WebUI)

#### NanoBot.WebUI.Hubs

- `class ChatHub` (NanoBot.WebUI)

#### NanoBot.WebUI.Middleware

- `record ErrorInfo` (NanoBot.WebUI)
- `enum ErrorType` (NanoBot.WebUI)
- `class UserFriendlyExceptionMiddleware` (NanoBot.WebUI)
- `class UserFriendlyExceptionMiddlewareExtensions` (NanoBot.WebUI)

#### NanoBot.WebUI.Services

- `class AgentService` (NanoBot.WebUI)
- `class AuthService` (NanoBot.WebUI)
- `interface IAuthService` (NanoBot.WebUI)
- `interface ILocalizationService` (NanoBot.WebUI)
- `interface IWebUIConfigService` (NanoBot.WebUI)
- `class LocalizationService` (NanoBot.WebUI)
- `record SessionImageItem` (NanoBot.WebUI)
- `class SessionService` (NanoBot.WebUI)
- `class WebUIConfigService` (NanoBot.WebUI)
