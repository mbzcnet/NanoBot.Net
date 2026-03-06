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
