# 2026-03-31

- **增强 OnboardCommand 交互式配置流程**：
  - **重构工作流**：将依赖安装（Playwright/OmniParser）从非交互模式移到配置菜单之前
    - 非交互模式：先安装依赖，再应用选项
    - 交互模式：先显示依赖安装提示，再进入完整配置菜单
  - **增强主配置菜单**：新增以下菜单项：
    - Memory Configuration（内存配置）
    - Security Configuration（安全配置）
    - MCP Configuration（MCP 服务器管理）
    - Heartbeat Configuration（心跳配置）
    - WebUI Configuration（WebUI 配置）
    - Agent Settings（智能体名称、时区）
  - **增强 Tools 配置**：
    - `ConfigureToolsAsync` 新增 OmniParser 子菜单入口
    - 新增 `ConfigureOmniParserAsync` - OmniParser 完整子菜单（安装/重装/配置/启用）
    - 新增 `ConfigurePlaywrightAsync` - Playwright 完整子菜单（安装/重装/启用）
    - 新增 `ConfigureRpaSettings` - RPA 设置（端口、自动启动、截图路径、优化参数）
    - 新增 `ConfigureRpaEnabled` - RPA 启用/禁用状态
    - 新增 `ConfigureBrowserEnabled` - 浏览器工具启用/禁用状态
    - 新增 skill 可用性辅助方法：`IsBrowserSkillAvailableAsync`、`IsRpaSkillAvailableAsync`、`IsOmniParserInstalledAsync`
    - 移除已废弃的 `ConfigureRap` 和 `GetToolStatus` 方法
  - **新增配置方法**：
    - `ConfigureMemoryAsync` - 内存开关、窗口、历史条目、指令字符限制
    - `ConfigureSecurity` - 工作区限制、Shell 超时、允许/拒绝目录
    - `ConfigureMcpAsync` - MCP 服务器增删改查
    - `ConfigureHeartbeatAsync` - 心跳开关、间隔、消息
    - `ConfigureWebUiAsync` - WebUI 多级子菜单（服务器/认证/CORS/安全/功能）
    - `ConfigureAgentSettingsAsync` - 智能体名称和时区
  - **移除 Start Web UI Mode 菜单项**：WebUI 通过 `nbot webui` 命令启动
  - 所有 OnboardCommandTests 通过

# 2026-03-29

- **修复 IDE0060 未使用参数警告**：
  - **StreamingProcessor.cs**：移除 `ProcessDirectStreamingAsync` 中未使用的 `chatId` 参数
  - **AgentRuntime.cs**：更新调用点，移除 `chatId` 参数传递
  - **MemoryContextProvider.cs**：`StoreAIContextAsync` 方法体为空，将 `context` 和 `cancellationToken` 参数前缀设为 `_` 表示有意不使用
  - **MemoryConsolidationContextProvider.cs**：`InvokingCoreAsync` 中未使用 `context` 参数，前缀设为 `_`
  - **SessionManager.cs**：经审查，`sessionJson` 参数在 `BuildMetadataLineAsync` 中被使用（line 475），无需修改

- **修复 IDE0052 未读取私有成员警告**：
  - **StreamingProcessor.cs**：移除未使用的 `_getChatClient` 字段和构造函数参数
  - **AgentRuntime.cs**：移除未使用的 `_sessionsDirectory` 和 `_innerSessionManager` 字段
  - **MemoryConsolidationContextProvider.cs**：为保留字段添加注释标记，表示为未来实现预留

# 2026-03-28

- **修复 WebUI Markdown 显示为源码**：
  - 移除 `md4x`（依赖外网 CDN + WASM，初始化失败时 `catch` 分支直接返回原文，被当作 `MarkupString` 注入 DOM）
  - 改用服务端 C# `Markdig` 直接渲染，零外部依赖
  - `MarkdownRenderer.razor` 重写：`Markdown.ToHtml(Content, _pipeline)` 在 `OnParametersSet()` 同步执行
  - `MarkdownPipeline` 启用 GFM 表格、自动链接、表情符号扩展
  - `App.razor` 移除 `md4x-loader.js` 引用，删除 `wwwroot/js/md4x-loader.js`

# 2026-03-28

- **修复 Skills 加载机制**：
  - `ISkillsLoader` 新增 `EnsureLoadedAsync()` 方法 - 确保 skills 被加载后再访问
  - `SkillsLoader` 实现 `EnsureLoadedAsync()` - 如果已加载则跳过，否则调用 `LoadAsync()`
  - `SkillsContextProvider.EnsureCacheAsync()` 调用 `EnsureLoadedAsync()` - 修复 skills 永远不会被加载到 AI context 的问题

- **修复 Workspace 初始化和 Memory 机制**：
  - **重构 OnboardCommand**：
    - 新增 `InitializeWorkspaceAsync()` 方法，使用 `WorkspaceManager.InitializeAsync()` 从嵌入资源正确提取 workspace 文件（模板 + skills）
    - 废弃 `CreateWorkspaceTemplatesAsync()` 方法，改为调用 `WorkspaceManager` 统一处理
  - **添加 HISTORY.md 支持**：
    - `IWorkspaceManager.GetHistoryFile()` — 新增方法
    - `WorkspaceConfig.GetHistoryFile()` — 返回 `memory/HISTORY.md` 路径
    - `IMemoryStore.AppendHistoryAsync()` — 追加 history 条目
    - `IMemoryStore.GetHistoryContext()` — 获取 history 上下文
    - `IMemoryStore.GetHistoryFilePath()` — 获取 history 文件路径
    - `MemoryStore` — 实现 history 相关方法
  - **完善 MemoryConfig**：
    - 新增 `EnableHistory` 属性（默认 true），控制 HISTORY.md 是否启用
  - **更新 MemoryContextProvider**：
    - 注入 history context 到 AI context
  - **完善 MemoryConsolidator**：
    - 添加 `AppendHistoryEntryAsync()` — LLM 总结成功后追加 history 条目
    - 添加 `RawArchiveAsync()` — LLM 失败时降级为 raw archive
    - 修复 prompt 要求返回 `history_entry` 和 `memory_update` 两个字段

# 2026-03-27

- **代码审核清理（基于 2026-03-27 代码审核报告）**：
  - **删除未使用的 BusMessageType.cs**：
    - 确认无引用后删除 `src/NanoBot.Core/Bus/BusMessageType.cs`
  - **统一配置读取到 ConfigurationLoader**：
    - `ChannelConfigService.cs` 改用 `ConfigurationLoader.LoadAsync()` 和 `ConfigurationLoader.SaveAsync()`
    - 保持 `ChannelsConfig` 单独保存逻辑
  - **更新 Feature-List.md 文档**：
    - 标记 `BusMessage`、`BusMessageType` 为 ❌ 已删除
    - 标记新增接口为 ✅ 已完成：`IMessageStore`、`ISkillsProvider`、`ISkillsMetadataProvider`、`IRpaExecutor`、`IRpaHealthProvider`、`IScreenAnalyzer`、`ScheduledJob`
    - 标记新增服务类为 ✅ 已完成：`MessageProcessor`、`StreamingProcessor`、`MemoryConsolidationService`、`SessionTitleManager`、`ImageContentProcessor`、`AgentExtensions`、`CliCommandContext`
    - 标记新增 WebUI 服务为 ✅ 已完成：`ConfigPaths`、`ChannelFormattingService`、`ChannelConfigRenderer`、`ChatFormattingService`、`SessionMessageParser`、`ChatMessage`、`ChatToolExecution`、`MessagePartsRenderer`
    - 更新功能统计：已完成 185+，已删除 2
  - **修复异步测试警告**：
    - `ChannelPluginTests.cs` 中两个测试方法改为 `async Task` 并使用 `await`
  - **添加单元测试**：
    - 新增 `tests/NanoBot.Agent.Tests/ToolHintFormatterTests.cs`，覆盖以下测试场景：
      - `FormatToolHint`（空集合、单调用、多调用、多参数截断）
      - `GetToolDescription`（read_file、write_file、unknown_tool、null 参数、web_search、browser）
      - `FormatToolResult`（错误 payload、内容 payload、快照 action、null 结果、空结果）
      - `GetFunctionResultPayload`（字符串结果、JsonElement）
      - `TruncateValue`（短字符串、长字符串截断）
      - `WrapToolHintAsMarkdown`

- **完成 NanoBot.WebUI 代码审计与重构优化（Phase 1-6）**：
  - **Phase 1 - 删除死模板 + 提取 ConfigPaths**：
    - 删除 `Counter.razor`、`Weather.razor` 模板页面
    - 新增 `Services/ConfigPaths.cs` — 统一配置路径解析，消除多文件重复代码
  - **Phase 2 - 修复循环依赖**：
    - 将完整 DI 链从 `NanoBot.Cli.Extensions` 迁移至 `NanoBot.Agent.Extensions`
    - `NanoBot.WebUI` 移除对 `NanoBot.Cli` 的项目引用
    - CLI 各调用点改用 `NanoBot.Agent.ServiceCollectionExtensions` 全限定名消除歧义
    - `NanoBot.Agent.csproj` 新增 `NanoBot.Tools`、`NanoBot.Channels` 项目引用和必要的包引用
  - **Phase 3 - 拆分 SessionService**：
    - 新增 `Services/SessionMessageParser.cs` — 消息解析核心逻辑（.jsonl 读取、Parts 构建、工具输出规范化、快照图片提取）
    - `SessionService.cs` 从 ~1230 行缩减至 ~270 行，专注于会话生命周期管理
  - **Phase 4 - 拆分 Chat.razor**：
    - 新增 `Components/Shared/ChatPageModels.cs` — `ChatMessage`、`ChatToolExecution`、`ProfileOption` 模型提取
    - 新增 `Services/ChatFormattingService.cs` — 静态格式化方法（文本、工具输出、Markdown 检测、错误识别、复制内容构建）
    - 新增 `Components/Shared/MessagePartsRenderer.razor` — Parts 交错渲染组件
    - `Chat.razor` 从 ~1050 行缩减至 ~743 行
  - **Phase 5 - Channels.razor 重构**：
    - 新增 `Services/ChannelFormattingService.cs` — 通道图标映射、启用/禁用、配置对象获取、验证消息
    - 新增 `Services/ChannelConfigRenderer.cs` — RenderFragment 工厂，动态渲染各通道配置编辑器组件
    - `Channels.razor` 移除 10 个 `Test*Connection` 方法、3 个 `On*ConfigChanged` 方法、`GetChannelIconName` 方法
    - `Channels.razor` 从 ~904 行缩减至 ~470 行
  - **Phase 6 - 警告清理**：
    - 消除 `@using NanoBot.Core.Configuration` 重复 using 指令警告（Channels、Config、ConfigProfiles、ConfigProfilesNew、ConfigProfileEdit 各页面）
    - 修复 `ConfigProfileEdit.razor` CS8974 警告：`Validation="@(ValidateProfileName)"` → `Validation="@((Func<string?, string?>)ValidateProfileName)"`
    - 修复 `Chat.razor` CS8601 null 赋值警告：所有 `chunk.ToolCallDetails.Name` 添加 `!` 和 `?? string.Empty`
    - `Program.cs` 移除无用的 `NanoBot.Cli.Extensions` 引用，改用 `NanoBot.Agent`
    - WebUI 项目：0 errors, 0 warnings

# 2026-03-20

- **创建原 nanobot 项目周报 (2026-03-13 ~ 2026-03-20)**：
  - 分析报告：`doc.ai/reports/update/20260320-nanobot-weekly-update.md`
  - 对比原项目最近 1 周（约 30+ commits）的更新内容
  - 主要更新领域：
    1. **稳定性修复**: Telegram 连接池分离和超时重试机制
    2. **功能增强**: Feishu 代码块支持、Slack 完成反应
    3. **Bug 修复**: 远程媒体 URL 验证、Cron 任务列表展示
    4. **代码质量**: Cron 工具重构和测试覆盖
  - 对齐状态汇总：
    - ✅ **已对齐**: Feishu 代码块支持、Slack 完成反应、Onboard 配置对齐、Feishu 媒体类型修复
    - ⚠️ **未对齐/需实现**: Telegram 连接池分离、Telegram 超时重试、Telegram 远程媒体 URL、Cron 列表展示增强、Provider 空 choices 处理、图片路径保留、Subagent 角色修复
  - 待办事项（高优先级）：
    1. Telegram 连接池分离 - 防止并发负载下的连接池耗尽
    2. Telegram 超时重试 - 提高消息发送可靠性
    3. Cron 列表展示增强 - 支持 schedule details 和 run state

# 2026-03-19

- **完成 RPA 工具设计实现**：
  - 新增 RPA 工具接口和模型 (`NanoBot.Core/Tools/Rpa/`):
    - `RpaModels.cs` - 操作类型枚举、数据模型（RpaAction 系列类、OmniParserResult 等）
    - `IRpaService.cs` - RPA 服务接口
    - `IInputSimulator.cs` - 输入模拟器接口
    - `IScreenCapture.cs` - 截图接口
  - 新增 RPA 服务实现 (`NanoBot.Infrastructure/Tools/Rpa/`):
    - `SharpHookInputSimulator.cs` - 基于 SharpHook 的鼠标/键盘模拟实现（支持 Windows/macOS/Linux）
    - `RpaService.cs` - RPA 服务核心实现（流程执行、操作解析）
    - `OmniParserClient.cs` - OmniParser HTTP 客户端
    - `OmniParserServiceManager.cs` - OmniParser 服务生命周期管理
    - `ScreenCaptureService.cs` - 截图服务（含平台特定实现工厂）
    - `ImageOptimizer.cs` - 截图优化（缩放 + JPEG 压缩，支持 25-187x 压缩比）
    - `Mac/MacScreenCapture.cs` - macOS CGWindowListCreateImage 截图实现
    - `Win/WinScreenCapture.cs` - Windows GDI BitBlt 截图实现
    - `Linux/LinuxScreenCapture.cs` - Linux XLib 截图实现
  - 新增 RPA 工具定义 (`NanoBot.Tools/BuiltIn/Rpa/RpaTools.cs`)
  - 新增 OmniParser Python 服务资源 (`NanoBot.Tools/Resources/omniparser/`):
    - `server.py` - Flask HTTP 服务，支持 /health、/parse、/parse/simple、/config 端点
    - `requirements.txt` - Python 依赖
  - 新增 RPA 配置 (`NanoBot.Core/Configuration/RpaToolsConfig.cs`):
    - `RpaToolsConfig` - RPA 工具配置（Enabled、InstallPath、ServicePort、AutoStartService 等）
    - `ScreenshotOptimizationConfig` - 截图优化配置
  - 更新 `OnboardCommand.cs` - 新增 OmniParser 安装选项 (`--skip-omniparser`)
  - 更新 `ToolProvider.cs` - 条件注册 RPA 工具
  - 更新 `AgentConfig.cs` - 添加 `Rpa` 配置属性
  - 更新 `ServiceCollectionExtensions.cs` - 添加 `AddRpaServices` 扩展方法
  - 功能支持：鼠标移动/点击/双击/右键/拖拽、键盘按键/组合键/文本输入、截图与 OmniParser 视觉分析

# 2026-03-18

- **修复工具调用失败问题**（关键修复）：
  - 问题：CLI 的 `agent -m` 命令不进行实际工具调用，LLM 输出文本格式的工具描述
  - 根本原因 1：`CompositeAIContextProvider` 返回 `Tools = context.AIContext.Tools` 导致工具被合并两次（AIContextProvider 框架会合并 input 和 provided 的 tools）
  - 根本原因 2：指令太长（9454 字符）导致 qwen3.5:4b 模型无法正确进行工具调用
  - 修复：
    1. `CompositeAIContextProvider.ProvideAIContextAsync` 返回 `Tools = null`，避免重复
    2. 在 `ChatOptions.Tools` 中设置工具（`AdditionalTools` 只用于本地调用，不发送给 LLM）
    3. 简化 `AGENTS.md` 指令，确保模型能正确理解工具使用场景
  - 验证：所有工具调用测试通过（8/8），CLI 工具调用正常工作

- 优化会话存储冗余：
  - 移除 metadata 行中的 `serializedSession` 存储，避免消息重复存储
  - metadata 行现在只包含元数据：`key`, `created_at`, `updated_at`, `title`, `profile_id`, `last_consolidated`
  - 消息只存储在普通 JSONL 行中
  - 简化读取逻辑：`GetMessagesAsync` 直接使用 `ReadMessagesFromJsonLines`，移除 `TryReadMessagesFromMetadata` 的消息读取逻辑
  - SessionManager 测试全部通过 (15/15)

# 2026-03-17

- 为 CLI agent 模式添加 `/model` 命令：
  - `/model` - 查看当前模型配置（Profile、Provider、Model、API Base）
  - `/model <profile-name>` - 切换到已配置的 profile
  - `/model <provider>/<model>` - 切换到特定模型（如 `/model openai/gpt-4o`）
  - `/model <model-name>` - 按模型名称模糊匹配
  - 显示可用 profiles 列表（当配置了多个 profiles 时）
  - 自动保存配置到 config.json
  - 注意：模型切换后需要重启 agent 才能生效

- 为 CLI agent 模式添加常用命令：
  - `/help`, `/?` - 显示帮助信息，列出所有可用命令
  - `/bye`, `/q` - 退出命令的快捷方式
  - 改进欢迎信息，提示用户使用 `/help` 查看命令
  - 美化的帮助信息输出（使用边框格式）

- 优化 `/model` 命令：
  - 支持数字快捷选择：`/model 1` 切换到第一个 profile
  - 模型切换立即生效，无需手动重启 agent
  - 显示 profiles 列表时带有编号 [1], [2], [3] 等

# 2026-03-15

- 统一 ValidationResult 类型：
  - 将 `GetSummary()` 方法从 WebUI 版本的 `ValidationResult` 迁移到 Core 版本
  - 删除 `WebUIConfigValidator.cs` 中的 `class ValidationResult` 定义
  - 更新 `WebUIConfigValidator.Validate()` 使用 Core 版本的 `record ValidationResult`
  - Core 版本的 `ValidationResult` 现在包含：`IsValid`, `HasWarnings`, `GetErrorMessage()`, `GetWarningMessage()`, `GetSummary()`
  - 消除了 `ValidationResult` 类型的重复定义，统一使用 `NanoBot.Core.Configuration.ValidationResult`

# 2026-03-15

- 实现 OpenCrawl 对齐计划 - Channel 插件架构：
  - 新增 `IChannelPlugin<TAccount>` 泛型接口，支持插件化架构
  - 新增适配器接口：`IChannelConfigAdapter`、`IChannelSecurityAdapter`、`IChannelOutboundAdapter`、`IChannelGroupAdapter`、`IChannelMentionAdapter`、`IChannelThreadingAdapter`、`IChannelStreamingAdapter`、`IChannelHeartbeatAdapter`
  - 新增 `ChannelCapabilities`、`ChannelPluginMeta`、`ChannelId` 等模型
  - 新增 `ChannelAccount` 多账户支持类及状态枚举
  - 新增 `ISecurityPolicy` 安全策略接口及 `DefaultSecurityPolicy` 默认实现
  - 新增 `IChannelPluginDiscoverer` 插件发现机制
  - 新增 Telegram 示例插件 `TelegramPlugin` 展示如何使用新接口
  - 更新 `ChannelManager` 支持插件注册
  - 新增单元测试：
    - `ChannelPluginTests.cs` - 插件发现和工厂测试
    - `SecurityPolicyTests.cs` - 安全策略各种规则测试（AllowAll、DenyAll、AllowList、BlockList 等）
  - 所有 56 个 Channel 测试通过

# 2026-03-14

- Agent CLI 多session支持：CLI现在支持多session管理，与WebUI使用相同的session存储位置。CLI新增交互命令：
  - `/new` 或 `/n` - 创建新session
  - `/list` 或 `/l` - 列出所有session
  - `/resume <id>` 或 `/r <id>` - 切换到指定session
  - `/clear` 或 `/c` - 清除当前session历史
  - `/sessions` 或 `/s` - 显示session列表
  - `/switch` - 交互式切换session
- `--session` 参数改为可选，不指定时自动使用上次使用的session
- 新增 `--list-sessions` 选项列出所有session后退出
- 统一session前缀为 `chat_`：CLI和WebUI现在共享相同的session存储（之前CLI用`cli:`，WebUI用`webui:`）
- 移除HISTORY.md机制：所有session消息统一保存到`sessions/*.jsonl`，不再写入`memory/HISTORY.md`
- 修复fallback截图路径：截图不再保存到`fallback_openclaw/screenshots`，而是使用`chat_default`作为默认session
- 修复工具调用失败问题：移除 SanitizingChatClient.cs 中 commit 9d5955b 引入的 orphaned tool message 过滤逻辑。
- 聊天頁新增「新對話」按鈕：頂部標題列可一鍵建立新會話並導向新聊天頁，生成中時按鈕停用。
- 修復用戶消息氣泡內邊距過大：`.nb-message-bubble-user` 與助手氣泡統一為 `padding: 20px 24px`。

# 2026-03-05

- 修复 browser snapshot 图片未显示问题：AgentRuntime 对工具结果 JSON 的字段解析改为大小写不敏感，兼容 Action/ImagePath 与 action/imagePath。
- 增强 browser 工具参数可选性：为 browser 工具的非必填参数补充默认值，避免模型省略参数时触发 required parameter 错误。
- 修复 exec 工具调用健壮性：为 timeoutSeconds 与 workingDir 增加默认值，避免 `required parameter 'timeoutSeconds'` 导致工具调用中断。
- 修复流式渲染缺失：WebUI 流式消费优先读取 `update.Text`，为空时回退拼接 `update.Contents` 中的 `TextContent`，确保工具注入文本（含 snapshot Markdown）可显示。
- 增加 snapshot 落盘日志：BrowserService 在截图保存成功/失败时输出 sessionKey、本地文件路径、相对路径与访问 URL，便于本地联调定位。
- 增强消息路径展示：assistant 消息在图片 Markdown 下追加 `snapshot-file-*` 本地路径文本，便于核对本地文件是否已生成。
- 新增百度首页 snapshot 集成测试：`BrowserService_BaiduSnapshot_CanSaveScreenshotToSessionFolder`，校验截图文件可落盘并输出相对路径、本地路径、访问 URL。
- 修复 macOS/Linux 截图保存失败：`System.Drawing` 不可用时自动回退保存原始截图字节，保证 snapshot 文件仍能写入会话目录并可展示。
- 将截图压缩实现迁移为 `SixLabors.ImageSharp`：移除 `System.Drawing.Common` 依赖，统一跨平台执行 50% 分辨率压缩并保存 PNG。
- 调整百度 snapshot 测试保留策略：新增 `NANOBOT_BROWSER_KEEP_ARTIFACTS=1` 开关，便于保留快照文件并直接核对本地路径。
- 增加本地图片读取接口：`GET /api/files/local?path=...`，用于本地服务场景按绝对路径读取图片并返回前端。
- 优化 snapshot URL 生成：当图片绝对路径不在当前 workspace sessions 下时，自动回退为 `/api/files/local?path=...`，避免图片丢失。
- 增加 sessionKey 兜底：snapshot 在缺失 sessionKey 时自动使用 `fallback:{profile}` 保存截图，避免因上下文缺失导致不落盘。
- 增强注入可观测性：在 AgentRuntime 增加 snapshot markdown 注入日志，并为图片块添加前后空行，降低与工具提示文本相互影响的概率。
- 新增回归测试：`BrowserService_SnapshotWithoutSessionKey_UsesFallbackAndSavesScreenshot`，验证无 sessionKey 场景仍可落盘并生成可访问相对路径。

# 2026-03-06

- 修复 snapshot 消息展示：移除 `snapshot-file-*` 本地路径文本输出，图片消息仅保留 Markdown 图片块并与后续文本使用空行分隔。
- 修复会话重载一致性：WebUI 重载时将 `tool_calls` 重建为工具提示文本，隐藏非 snapshot 的 tool JSON 结果，并合并连续 assistant/tool 消息为单气泡。
- 修复消息时间回放：重载消息优先读取 jsonl 中的 `timestamp`，避免刷新后时间全部变为当前时间。
- 修复流式中断落盘：`ProcessDirectStreamingAsync` 在 `finally` 中持久化会话，确保取消/异常场景下已产生内容可被重载恢复。
- 修复截图目录归属：browser 工具对空白 `sessionKey` 回退上下文会话键，`BrowserService` 对 `webui:{sessionId}` 归一化为 `{sessionId}/screenshots` 目录，避免写入 `fallback_openclaw`。
- 统一工具提示分段渲染：流式注入与历史回放均改为 `nb-tool-hint` HTML 块，避免 Markdown 软换行吞并造成工具调用与正文粘连。
- 收紧 tool 结果回放策略：重载时仅保留 snapshot/capture 对应图片，其他 tool 纯文本与错误内容不再回放到对话气泡，提升与实时视图一致性。
- 修复重载图片丢失：会话回放解析 tool 结果时新增双层 JSON 与 `\u0022` 转义修复，兼容 `"{\"action\":\"snapshot\"...}"` 与 `{\\u0022action\\u0022...}` 格式并正确提取 `imagePath`。

# 2026-03-10

- 实现增强版文件编辑工具（FileTools Enhancement）：
  - 新增 8 种文本匹配策略：精确匹配、行修剪匹配、块锚点匹配、空白规范化、缩进灵活匹配、转义规范化、边界修剪匹配、上下文感知匹配
  - 新增行尾规范化处理（CRLF/LF 自动转换）
  - 新增模糊匹配建议功能，匹配失败时提供最佳匹配位置提示
  - 新增文件大小限制保护（默认 128KB 字符限制）
  - 新增二进制文件检测（扩展名 + 内容采样）
  - 新增差异生成和 diff 预览功能
  - 配置支持通过 `FileToolsConfig.UseEnhanced` 开关切换新旧实现
  - 所有更改向后兼容，现有 FileTools API 保持不变
- 创建 FileTools 增强设计文档：doc.ai/solutions/design/FileTools-Enhancement.md
- 新增配置类：FileToolsConfig、FileReadConfig、FileEditConfig
- 所有 25 个现有 FileTools 测试通过

# 2026-03-10

- 创建 WebUI 增强实现方案：基于 OpenCode 对比分析报告，制定 6 项核心功能增强方案
  - 实时同步 (Real-time Sync)：基于 SignalR 的多客户端同步机制
  - 密码保护 (Password Protection)：BCrypt 会话级密码保护
  - 过期时间 (Expiration Time)：会话过期自动清理机制
  - 快捷键 (Keyboard Shortcuts)：全局快捷键支持
  - 拖拽上传 (Drag & Drop Upload)：文件拖拽上传功能
  - 消息样式优化 (Message Style Enhancement)：增强 Markdown 渲染和交互
- 更新功能清单 (Feature-List.md)：添加 8 个新功能项，状态为计划中

# 2026-03-07

- 修复多模态图片注入：AgentRuntime 解析用户消息中的 Markdown 图片 URL，并将会话本地图片作为二进制内容附加到用户消息，确保模型可真实接收图片输入。
- 优化历史会话图片持久化：SessionManager 在保存用户消息时自动生成缩略图，历史记录中将原图替换为“缩略图可点击打开原图”的 Markdown 链接。
- 增强历史图片元数据：SessionManager 为消息新增 `images` 元数据字段，记录原图 URL、缩略图 URL、概述摘要、尺寸、MIME 与文件大小。
- 增强历史回放展示：SessionService 解析 `images` 元数据并在消息中追加“图片概述”展示块，同时将图片元数据映射到消息附件结构。
- 优化聊天图片展示尺寸：Markdown 图片样式新增 `max-height` 与 `object-fit` 限制，避免对话中按原图尺寸撑开界面。
- 修复样式未命中问题：MarkdownRenderer 在 CSS 隔离场景下改用 `::deep .markdown-content img` 规则，确保动态渲染的 Markdown 图片也能应用最大高度限制。
