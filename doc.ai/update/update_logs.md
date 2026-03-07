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

# 2026-03-07

- 修复多模态图片注入：AgentRuntime 解析用户消息中的 Markdown 图片 URL，并将会话本地图片作为二进制内容附加到用户消息，确保模型可真实接收图片输入。
- 优化历史会话图片持久化：SessionManager 在保存用户消息时自动生成缩略图，历史记录中将原图替换为“缩略图可点击打开原图”的 Markdown 链接。
- 增强历史图片元数据：SessionManager 为消息新增 `images` 元数据字段，记录原图 URL、缩略图 URL、概述摘要、尺寸、MIME 与文件大小。
- 增强历史回放展示：SessionService 解析 `images` 元数据并在消息中追加“图片概述”展示块，同时将图片元数据映射到消息附件结构。
- 优化聊天图片展示尺寸：Markdown 图片样式新增 `max-height` 与 `object-fit` 限制，避免对话中按原图尺寸撑开界面。
