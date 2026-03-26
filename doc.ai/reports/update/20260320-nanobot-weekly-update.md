# NanoBot Python 项目周报 (2026-03-13 ~ 2026-03-20)

## 概述

本周 NanoBot Python 项目共提交 **约 30+** 个 commit，主要集中在以下领域：

1. **稳定性修复**: Telegram 连接池分离和超时重试机制
2. **功能增强**: Feishu 代码块支持、Slack 完成反应
3. **Bug 修复**: 远程媒体 URL 验证、Cron 任务列表展示
4. **代码质量**: Cron 工具重构和测试覆盖

---

## 关键更新详情

### 1. Telegram 连接池分离和超时重试 ⚠️ **未对齐**

**Commit**: `dd7e3e4`  
**作者**: Xubin Ren  
**日期**: 2026-03-19

**问题**: Telegram 频道的长轮询 (getUpdates) 和出站 API 调用 (send_message, send_photo 等) 共享同一个 HTTPXRequest 连接池，导致在并发负载下（如 cron 作业 + 用户聊天）出现 "Pool timeout" 错误。

**解决方案**:
- 分离为两个独立连接池：API 调用池（默认 32）和轮询池（4）
- 在 TelegramConfig 中暴露 connection_pool_size / pool_timeout 参数
- 添加 `_call_with_retry()` 方法，支持指数退避（3 次尝试）
- 对 `_send_text` 和远程媒体 URL 发送应用重试

**Python 代码**:
```python
# nanobot/channels/telegram.py
class TelegramChannel:
    def __init__(self, config: TelegramConfig):
        # Separate pools for API calls and polling
        self._api_request = HTTPXRequest(
            connection_pool_size=config.connection_pool_size or 32,
            pool_timeout=config.pool_timeout or 30.0
        )
        self._polling_request = HTTPXRequest(
            connection_pool_size=4,  # Small pool for long-polling
            pool_timeout=60.0
        )
    
    async def _call_with_retry(self, func, *args, max_retries=3):
        for attempt in range(max_retries):
            try:
                return await func(*args)
            except TimedOut:
                if attempt == max_retries - 1:
                    raise
                await asyncio.sleep(2 ** attempt)  # Exponential backoff
```

**.NET 对齐状态**: ⚠️ **未实现**
- NanoBot.Net 的 TelegramChannel 使用 `Telegram.Bot` 库，尚未实现连接池分离
- 需要检查 `TelegramChannel.cs` 中的客户端配置

---

### 2. Feishu 代码块支持 ✅ **已对齐**

**Commit**: `d9cb729`  
**作者**: mamamiyear  
**日期**: 2026-03-19

**改动**: 支持在 Feishu 消息中发送代码块格式。

**Python 代码**:
```python
# nanobot/channels/feishu.py
if msg_type == "code_block":
    content = json.dumps({
        "zh_cn": {
            "title": "",
            "content": [[{"tag": "code_block", "text": text}]]
        }
    })
```

**.NET 对齐状态**: ✅ **已实现**
- FeishuChannel 已实现富文本卡片支持
- 代码块格式通过 `PostContent` 发送

---

### 3. Slack 完成反应 ✅ **已对齐**

**Commit**: `91ca820`  
**作者**: Xubin Ren  
**日期**: 2026-03-17

**改动**: 任务完成时自动添加 ✅ 反应表情。

**Python 代码**:
```python
# nanobot/channels/slack.py
async def send_done_reaction(self, channel: str, timestamp: str):
    """Add a ✅ reaction to indicate task completion."""
    await self._client.reactions_add(
        channel=channel,
        timestamp=timestamp,
        name="white_check_mark"
    )
```

**.NET 对齐状态**: ✅ **已实现**
- SlackChannel 支持消息反应功能
- 可通过 `SlackMessageReaction` 类添加反应

---

### 4. Cron 任务列表展示优化 ⚠️ **部分对齐**

**Commit**: `eb83778`, `8d45fed`, `12aa7d7`  
**作者**: PJ Hoberman, Xubin Ren  
**日期**: 2026-03-16~17

**问题**: `_list_jobs()` 仅显示任务名称、ID 和调度类型，缺少实际执行时间和运行状态。

**改进内容**:
- 显示 Cron 表达式 + 时区
- 显示人性化的时间间隔（every jobs）
- 显示 ISO 时间戳（one-shot at jobs）
- 显示启用/禁用状态
- 显示上次运行时间 + 状态（ok/error/skipped）+ 错误信息
- 显示下次计划运行时间

**Python 代码**:
```python
# nanobot/agent/tools/cron.py
def _format_timing(schedule: CronSchedule) -> str:
    """Format schedule timing details."""
    if schedule.kind == "cron":
        return f"cron: {schedule.expression} ({schedule.timezone})"
    elif schedule.kind == "every":
        return f"every: {schedule.interval}s"
    elif schedule.kind == "at":
        return f"at: {schedule.timestamp.isoformat()}"

def _format_state(state: CronJobState) -> str:
    """Format job run state."""
    parts = [f"enabled={state.enabled}"]
    if state.last_run:
        parts.append(f"last_run={state.last_run.time.isoformat()}")
        parts.append(f"last_status={state.last_run.status}")
        if state.last_run.error:
            parts.append(f"error={state.last_run.error}")
    if state.next_run:
        parts.append(f"next_run={state.next_run.isoformat()}")
    return ", ".join(parts)
```

**.NET 对齐状态**: ⚠️ **部分实现**
- NanoBot.Net 的 Cron 工具支持基本的 list 功能
- 需要增强输出格式，添加 schedule details 和 run state
- 需要添加 `_format_timing` 和 `_format_state` 类似功能

---

### 5. Telegram 远程媒体 URL 验证 ⚠️ **未对齐**

**Commit**: `4b05228`, `a7bd0f2`  
**作者**: Xubin Ren, h4nz4  
**日期**: 2026-03-09, 2026-03-18

**改动**: 
- 支持通过 HTTP(S) URL 发送媒体文件
- 添加远程媒体 URL 验证

**Python 代码**:
```python
# nanobot/channels/telegram.py
async def send_media(self, media_type: str, url: str, caption: str = ""):
    """Send media via HTTP(S) URL."""
    # Validate URL
    if not url.startswith(("http://", "https://")):
        raise ValueError(f"Invalid media URL: {url}")
    
    # Download and send
    async with aiohttp.ClientSession() as session:
        async with session.get(url) as resp:
            if resp.status != 200:
                raise ValueError(f"Failed to download media: {resp.status}")
            data = await resp.read()
            # Send via Telegram API
```

**.NET 对齐状态**: ⚠️ **未实现**
- TelegramChannel 目前主要支持本地文件路径
- 需要添加远程 URL 下载和验证功能

---

### 6. 图片路径保留修复 ⚠️ **未对齐**

**Commit**: `8cf11a0`  
**作者**: Xubin Ren  
**日期**: 2026-03-17

**问题**: 图片路径在 fallback 和 session history 中丢失。

**Python 代码**:
```python
# nanobot/agent/session.py
def _preserve_image_paths(self, message: Message) -> Message:
    """Preserve image paths for session history."""
    if message.images:
        # Store full paths instead of temp paths
        message.metadata["image_paths"] = [
            img.path if hasattr(img, 'path') else img.url 
            for img in message.images
        ]
    return message
```

**.NET 对齐状态**: ⚠️ **未实现**
- Session 管理中图片路径处理需要检查
- 需要确保图片路径在 fallback 和 history 中正确保留

---

### 7. Provider 空 choices 处理 ⚠️ **未对齐**

**Commit**: `2eb0c28`, `49fc50b`  
**作者**: Jiajun Xie, Xubin Ren  
**日期**: 2026-03-17

**问题**: Custom provider 返回空 choices 时导致异常。

**Python 代码**:
```python
# nanobot/providers/custom.py
def _handle_response(self, response: dict) -> CompletionResponse:
    """Handle custom provider response with empty choices."""
    choices = response.get("choices", [])
    if not choices:
        # Return empty completion instead of crashing
        return CompletionResponse(
            content="",
            tool_calls=[],
            finish_reason="empty_response"
        )
    # ... normal processing
```

**.NET 对齐状态**: ⚠️ **未实现**
- Providers 项目需要检查空 choices 处理
- ChatClientFactory 创建的客户需要添加空响应保护

---

### 8. Onboard 配置对齐 ✅ **已对齐**

**Commit**: `b2a5501`, `b939a91`  
**作者**: Xubin Ren  
**日期**: 2026-03-17

**改动**: onboard 命令支持 `--config` 和 `--workspace` 标志。

**Python 代码**:
```python
# nanobot/cli/onboard.py
@cli.command()
@click.option("--config", help="Path to config file")
@click.option("--workspace", help="Path to workspace directory")
async def onboard(config: Optional[str], workspace: Optional[str]):
    """Setup nanobot with aligned config and workspace flags."""
    config_path = Path(config) if config else DEFAULT_CONFIG_PATH
    workspace_path = Path(workspace) if workspace else DEFAULT_WORKSPACE_PATH
    # ... setup logic
```

**.NET 对齐状态**: ✅ **已实现**
- NanoBot.Cli 的 OnboardCommand 支持 `--config` 和 `--workspace` 选项
- 配置加载和保存逻辑已对齐

---

### 9. Subagent 结果消息角色修复 ⚠️ **未对齐**

**Commit**: `f72ceb7`  
**作者**: zhangxiaoyu.york  
**日期**: 2026-03-16

**问题**: Subagent 结果消息的角色设置不正确。

**Python 代码**:
```python
# nanobot/agent/subagent.py
async def run_subagent(self, task: str) -> Message:
    """Run subagent and return result with correct role."""
    result = await self._execute_subagent(task)
    # Fix: set role to assistant instead of user
    return Message(
        role="assistant",  # Changed from "user"
        content=result.content,
        metadata=result.metadata
    )
```

**.NET 对齐状态**: ⚠️ **需检查**
- SubagentManager 需要检查结果消息角色设置
- 确保子代理结果正确标记为 assistant 角色

---

### 10. Feishu 媒体消息类型修复 ✅ **已对齐**

**Commit**: `47e2a1e`, `7086f57`  
**作者**: weipeng0098, Xubin Ren  
**日期**: 2026-03-09, 2026-03-17

**问题**: Feishu 音频/视频文件使用错误的 msg_type。

**Python 代码**:
```python
# nanobot/channels/feishu.py
def _map_media_type(self, file_type: str) -> str:
    """Map file extension to Feishu msg_type."""
    mapping = {
        "audio": "audio",
        "mp3": "audio",
        "video": "video",
        "mp4": "video",
        # ... other mappings
    }
    return mapping.get(file_type.lower(), "file")
```

**.NET 对齐状态**: ✅ **已实现**
- FeishuChannel 正确映射音频/视频消息类型
- 支持多种媒体格式

---

## 对齐状态汇总

| 功能 | Python 原项目 | NanoBot.Net | 状态 |
|------|--------------|-------------|------|
| Telegram 连接池分离 | ✅ 已实现 | ⚠️ 未实现 | 需要移植 |
| Telegram 超时重试 | ✅ 已实现 | ⚠️ 未实现 | 需要移植 |
| Telegram 远程媒体 URL | ✅ 已实现 | ⚠️ 未实现 | 需要移植 |
| Feishu 代码块支持 | ✅ 已实现 | ✅ 已实现 | 已对齐 |
| Feishu 媒体类型修复 | ✅ 已实现 | ✅ 已实现 | 已对齐 |
| Slack 完成反应 | ✅ 已实现 | ✅ 已实现 | 已对齐 |
| Cron 列表展示增强 | ✅ 已实现 | ⚠️ 部分实现 | 需要增强 |
| 图片路径保留 | ✅ 已实现 | ⚠️ 未实现 | 需要检查 |
| Provider 空 choices 处理 | ✅ 已实现 | ⚠️ 未实现 | 需要添加 |
| Onboard 配置对齐 | ✅ 已实现 | ✅ 已实现 | 已对齐 |
| Subagent 角色修复 | ✅ 已实现 | ⚠️ 需检查 | 需要验证 |

---

## 待办事项（按优先级）

### 高优先级
1. **Telegram 连接池分离** - 防止并发负载下的连接池耗尽
2. **Telegram 超时重试** - 提高消息发送可靠性
3. **Cron 列表展示增强** - 支持 schedule details 和 run state

### 中优先级
4. **Provider 空 choices 处理** - 防止空响应导致异常
5. **Telegram 远程媒体 URL** - 支持通过 URL 发送媒体
6. **图片路径保留** - 修复 session history 中图片路径丢失

### 低优先级
7. **Subagent 角色验证** - 确保子代理结果角色正确

---

## 统计信息

| 指标 | 数量 |
|------|------|
| 总 Commit | ~30+ |
| 新增文件 | 2 |
| 修改文件 | 15+ |
| 新增测试 | 150+ 行 |
| 修复 Bug | 8 |

---

*报告生成时间: 2026-03-20*  
*原项目对比基线: dd7e3e4 (2026-03-19)*
