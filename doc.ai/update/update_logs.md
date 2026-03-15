# 2026-03-14

- Agent CLI 多session支持：CLI现在支持多session管理，与WebUI使用相同的session存储位置。CLI session使用`cli:`前缀，WebUI使用`webui:`前缀，两者可独立管理。CLI新增交互命令：
  - `/new` 或 `/n` - 创建新session
  - `/list` 或 `/l` - 列出所有CLI session
  - `/resume <id>` 或 `/r <id>` - 切换到指定session
  - `/clear` 或 `/c` - 清除当前session历史
  - `/sessions` 或 `/s` - 显示session列表
  - `/switch` - 交互式切换session
- `--session` 参数改为可选，不指定时自动使用上次使用的session
- 新增 `--list-sessions` 选项列出所有session后退出
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
