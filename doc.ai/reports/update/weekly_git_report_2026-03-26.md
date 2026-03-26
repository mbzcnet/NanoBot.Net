# NanoBot 最近一周 GIT 提交报告

**报告生成时间**: 2026-03-26
**报告周期**: 2026-03-19 至 2026-03-26
**提交总数**: 21 条

---

## 1. Telegram 流式消息边界修复

**提交**: `33abe915e767f64e43b4392a4658815862d2e5f4`
**日期**: 2026-03-26
**作者**: Xubin Ren

### 变更摘要
修复了 Telegram 流式消息边界问题，改进了流式消息的分段处理机制。

### 关键代码变更

**nanobot/agent/loop.py**
```python
# 新增流式消息分段ID生成机制
stream_base_id = f"{msg.session_key}:{time.time_ns()}"
stream_segment = 0

def _current_stream_id() -> str:
    return f"{stream_base_id}:{stream_segment}"

# 在流式回调中包含 stream_id
async def on_stream(delta: str) -> None:
    await self.bus.publish_outbound(OutboundMessage(
        channel=msg.channel, chat_id=msg.chat_id,
        content=delta,
        metadata={
            "_stream_delta": True,
            "_stream_id": _current_stream_id(),
        },
    ))

async def on_stream_end(*, resuming: bool = False) -> None:
    nonlocal stream_segment
    await self.bus.publish_outbound(OutboundMessage(
        channel=msg.channel, chat_id=msg.chat_id,
        content="",
        metadata={
            "_stream_end": True,
            "_resuming": resuming,
            "_stream_id": _current_stream_id(),
        },
    ))
    stream_segment += 1
```

**nanobot/channels/telegram.py**
```python
# 新增流式缓冲区 stream_id 追踪
class _StreamBuf:
    text: str = ""
    message_id: int | None = None
    last_edit: float = 0.0
    stream_id: str | None = None  # 新增

# 处理消息未修改错误的辅助方法
@staticmethod
def _is_not_modified_error(exc: Exception) -> bool:
    return isinstance(exc, BadRequest) and "message is not modified" in str(exc).lower()

# 在 send_delta 中使用 stream_id 进行分段验证
if stream_id is not None and buf.stream_id is not None and buf.stream_id != stream_id:
    return
```

---

## 2. 新增 Step Fun (阶跃星辰) 提供商支持

**提交**: `813de554c9b08e375fc52eebc96c28d7c2faf5c2`
**日期**: 2026-03-25
**作者**: longyongshen

### 变更摘要
新增对 Step Fun (阶跃星辰) LLM 提供商的支持，使用 OpenAI 兼容 API。

### 关键代码变更

**nanobot/providers/registry.py**
```python
# Step Fun (阶跃星辰): OpenAI-compatible API
ProviderSpec(
    name="stepfun",
    keywords=("stepfun", "step"),
    env_key="STEPFUN_API_KEY",
    display_name="Step Fun",
    backend="openai_compat",
    default_api_base="https://api.stepfun.com/v1",
),
```

**nanobot/config/schema.py**
```python
class ProvidersConfig(Base):
    # ... 其他提供商 ...
    stepfun: ProviderConfig = Field(default_factory=ProviderConfig)  # Step Fun (阶跃星辰)
```

### 配置说明
- 大陆用户设置: `"apiBase": "https://api.stepfun.com/v1"`
- 专属优惠链接: [Overseas](https://platform.stepfun.ai/step-plan) · [Mainland China](https://platform.stepfun.com/step-plan)

---

## 3. 消息发送重试机制（指数退避）

**提交**: `5e9fa28ff271ff8a521c93e17e68e4dbf09c40da`
**日期**: 2026-03-25
**作者**: chengyongru

### 变更摘要
新增消息发送失败时的自动重试机制，支持指数退避策略。

### 关键代码变更

**nanobot/config/schema.py**
```python
class ChannelsConfig(Base):
    send_progress: bool = True  # stream agent's text progress to the channel
    send_tool_hints: bool = False  # stream tool-call hints
    send_max_retries: int = Field(default=3, ge=0, le=10)  # 最大重试次数
```

**nanobot/channels/manager.py**
```python
# 重试延迟配置（指数退避: 1s, 2s, 4s）
_SEND_RETRY_DELAYS = (1, 2, 4)

async def _send_with_retry(self, channel: BaseChannel, msg: OutboundMessage) -> None:
    """Send a message with retry on failure using exponential backoff."""
    max_attempts = max(self.config.channels.send_max_retries, 1)

    for attempt in range(max_attempts):
        try:
            await self._send_once(channel, msg)
            return  # Send succeeded
        except asyncio.CancelledError:
            raise  # Propagate cancellation for graceful shutdown
        except Exception as e:
            if attempt == max_attempts - 1:
                logger.error(
                    "Failed to send to {} after {} attempts: {} - {}",
                    msg.channel, max_attempts, type(e).__name__, e
                )
                return
            delay = _SEND_RETRY_DELAYS[min(attempt, len(_SEND_RETRY_DELAYS) - 1)]
            logger.warning(
                "Send to {} failed (attempt {}/{}): {}, retrying in {}s",
                msg.channel, attempt + 1, max_attempts, type(e).__name__, delay
            )
            try:
                await asyncio.sleep(delay)
            except asyncio.CancelledError:
                raise
```

### 重试行为
- **尝试 1**: 初始发送
- **尝试 2-4**: 重试延迟分别为 1s, 2s, 4s
- **尝试 5+**: 延迟上限为 4s

---

## 4. 时区配置支持

**提交**: `13d6c0ae52e8604009e79bbcf8975618551dcf3d`
**日期**: 2026-03-25
**作者**: Xubin Ren

### 变更摘要
新增可配置的时区支持，影响运行时上下文和心跳提示的时间显示。

### 关键代码变更

**nanobot/config/schema.py**
```python
class AgentDefaults(Base):
    temperature: float = 0.1
    max_tool_iterations: int = 40
    reasoning_effort: str | None = None
    timezone: str = "UTC"  # IANA 时区，如 "Asia/Shanghai", "America/New_York"
```

**nanobot/utils/helpers.py**
```python
def current_time_str(timezone: str | None = None) -> str:
    """Human-readable current time with weekday and UTC offset."""
    from zoneinfo import ZoneInfo

    try:
        tz = ZoneInfo(timezone) if timezone else None
    except (KeyError, Exception):
        tz = None

    now = datetime.now(tz=tz) if tz else datetime.now().astimezone()
    offset = now.strftime("%z")
    offset_fmt = f"{offset[:3]}:{offset[3:]}" if len(offset) == 5 else offset
    tz_name = timezone or (time.strftime("%Z") or "UTC")
    return f"{now.strftime('%Y-%m-%d %H:%M (%A)')} ({tz_name}, UTC{offset_fmt})"
```

**nanobot/agent/context.py**
```python
def __init__(self, workspace: Path, timezone: str | None = None):
    self.workspace = workspace
    self.timezone = timezone  # 新增时区参数
    # ...

@staticmethod
def _build_runtime_context(
    channel: str | None, chat_id: str | None, timezone: str | None = None,
) -> str:
    """Build untrusted runtime metadata block for injection before the user message."""
    lines = [f"Current Time: {current_time_str(timezone)}"]
    # ...
```

### 配置示例
```json
{
  "agents": {
    "defaults": {
      "timezone": "Asia/Shanghai"
    }
  }
}
```

---

## 5. Cron 工具时区继承

**提交**: `4a7d7b88236cd9a84975888fb4b347aff844985b`
**日期**: 2026-03-25
**作者**: Xubin Ren

### 变更摘要
使 Cron 调度使用配置的代理时区作为默认值。

### 关键代码变更

**nanobot/agent/tools/cron.py**
```python
def __init__(self, cron_service: CronService, default_timezone: str = "UTC"):
    self._cron = cron_service
    self._default_timezone = default_timezone  # 新增默认时区

# 验证时区有效性的辅助方法
@staticmethod
def _validate_timezone(tz: str) -> str | None:
    from zoneinfo import ZoneInfo
    try:
        ZoneInfo(tz)
    except (KeyError, Exception):
        return f"Error: unknown timezone '{tz}'"
    return None

# 创建定时任务时使用默认时区
if cron_expr:
    effective_tz = tz or self._default_timezone
    if err := self._validate_timezone(effective_tz):
        return err
    schedule = CronSchedule(kind="cron", expr=cron_expr, tz=effective_tz)
elif at:
    # ...
    if dt.tzinfo is None:
        if err := self._validate_timezone(self._default_timezone):
            return err
        dt = dt.replace(tzinfo=ZoneInfo(self._default_timezone))
```

---

## 6. Cron 显示时间对齐时区

**提交**: `fab14696a97c8ad07f1c041e208f0b02a381b8ed`
**日期**: 2026-03-25
**作者**: Xubin Ren

### 变更摘要
使 Cron 列表输出在调度时区上下文中渲染一次性任务和运行状态时间戳。

### 关键代码变更

**nanobot/agent/tools/cron.py**
```python
def _display_timezone(self, schedule: CronSchedule) -> str:
    """Pick the most human-meaningful timezone for display."""
    return schedule.tz or self._default_timezone

@staticmethod
def _format_timestamp(ms: int, tz_name: str) -> str:
    from zoneinfo import ZoneInfo
    dt = datetime.fromtimestamp(ms / 1000, tz=ZoneInfo(tz_name))
    return f"{dt.isoformat()} ({tz_name})"

def _format_state(self, state: CronJobState, schedule: CronSchedule) -> list[str]:
    """Format job run state as display lines."""
    lines: list[str] = []
    display_tz = self._display_timezone(schedule)
    if state.last_run_at_ms:
        info = (
            f"  Last run: {self._format_timestamp(state.last_run_at_ms, display_tz)}"
            f" — {state.last_status or 'unknown'}"
        )
        # ...
    if state.next_run_at_ms:
        lines.append(f"  Next run: {self._format_timestamp(state.next_run_at_ms, display_tz)}")
    return lines
```

---

## 7. OpenAI o1 兼容性修复

**提交**: `ef10df9acb27cad69f6064e59fd8071d2ab0143e`
**日期**: 2026-03-25
**作者**: flobo3

### 变更摘要
添加 `max_completion_tokens` 参数以支持 OpenAI o1 模型兼容性。

### 关键代码变更

**nanobot/providers/openai_compat_provider.py**
```python
def _prepare_request_kwargs(...) -> dict[str, Any]:
    kwargs: dict[str, Any] = {
        "model": model_name,
        "messages": self._sanitize_messages(self._sanitize_empty_content(messages)),
        "max_tokens": max(1, max_tokens),
        "max_completion_tokens": max(1, max_tokens),  # 新增：o1 兼容性
        "temperature": temperature,
    }
```

---

## 8. Gemini thought_signature 往返支持

**提交**: `af84b1b8c0278f4c3a2fa208ebf1efbad54953e1`, `b5302b6f3da12e39caad98e9a82fce47880d5c77`
**日期**: 2026-03-25
**作者**: Yohei Nishikubo, Xubin Ren

### 变更摘要
支持 Gemini OpenAI 兼容性 API 的 thought_signature 字段往返传输，通过 `extra_content` 字段保留提供商特定数据。

### 关键代码变更

**nanobot/providers/base.py**
```python
@dataclass
class ToolCallRequest:
    id: str
    name: str
    arguments: dict[str, Any]
    extra_content: dict[str, Any] | None = None  # 新增
    provider_specific_fields: dict[str, Any] | None = None
    function_provider_specific_fields: dict[str, Any] | None = None

    def to_openai_tool_call(self) -> dict[str, Any]:
        tool_call = {
            "type": "function",
            "id": self.id,
            "function": {
                "name": self.name,
                "arguments": json.dumps(self.arguments, ensure_ascii=False),
            },
        }
        if self.extra_content:
            tool_call["extra_content"] = self.extra_content
        # ...
```

**nanobot/providers/openai_compat_provider.py**
```python
def _extract_tc_extras(tc: Any) -> tuple[
    dict[str, Any] | None,
    dict[str, Any] | None,
    dict[str, Any] | None,
]:
    """Extract (extra_content, provider_specific_fields, fn_provider_specific_fields)."""
    extra_content = _coerce_dict(_get(tc, "extra_content"))
    # ...
    return extra_content, prov, fn_prov
```

---

## 9. OpenAI 兼容响应处理增强

**提交**: `263069583d921a30858de6e58e03f49b0fd12703`
**日期**: 2026-03-25
**作者**: Xubin Ren

### 变更摘要
增强对非标准 OpenAI 兼容后端的响应处理，支持字符串和字典格式的响应。

### 关键代码变更

**nanobot/providers/openai_compat_provider.py**
```python
def _parse(self, response: Any) -> LLMResponse:
    # 支持纯字符串响应
    if isinstance(response, str):
        return LLMResponse(content=response, finish_reason="stop")

    response_map = self._maybe_mapping(response)
    if response_map is not None:
        choices = response_map.get("choices") or []
        if not choices:
            # 处理空 choices 但包含 content/output_text 的情况
            content = self._extract_text_content(
                response_map.get("content") or response_map.get("output_text")
            )
            if content is not None:
                return LLMResponse(
                    content=content,
                    finish_reason=str(response_map.get("finish_reason") or "stop"),
                    usage=self._extract_usage(response_map),
                )
```

---

## 10. 微信 (WeiXin) 多项修复

### 10.1 会话持久化修复
**提交**: `1f5492ea9e33d431852b967b058d2c48d40ef8fb`
**日期**: 2026-03-24
**作者**: xcosmosbox

```python
# 保存 context_tokens 到 account.json
def _save_state(self) -> None:
    data = {
        "token": self._token,
        "get_updates_buf": self._get_updates_buf,
        "context_tokens": self._context_tokens,  # 新增
        "base_url": self.config.base_url,
    }
    state_file.write_text(json.dumps(data, ensure_ascii=False))
```

### 10.2 轮询问题修复
**提交**: `9c872c34584b32bc72c6af0e4922263fa3d3315f`
**日期**: 2026-03-24
**作者**: xcosmosbox

```python
def _pause_session(self, duration_s: int = SESSION_PAUSE_DURATION_S) -> None:
    """暂停会话，防止过期会话重复重试"""
    self._session_pause_until = time.time() + duration_s

async def _poll_once(self) -> None:
    remaining = self._session_pause_remaining_s()
    if remaining > 0:
        logger.warning("WeChat session paused, waiting {} min before next poll.", ...)
        await asyncio.sleep(remaining)
        return
```

### 10.3 QR 码自动刷新
**提交**: `48902ae95a67fc465ec394448cda9951cb32a84a`
**日期**: 2026-03-24
**作者**: xcosmosbox

```python
MAX_QR_REFRESH_COUNT = 3

async def _qr_login(self) -> bool:
    refresh_count = 0
    qrcode_id, scan_url = await self._fetch_qr_code()
    self._print_qr_code(scan_url)

    while True:
        # ...
        elif status == "expired":
            refresh_count += 1
            if refresh_count > MAX_QR_REFRESH_COUNT:
                logger.warning("QR code expired too many times, giving up.")
                return False
            logger.warning("QR code expired, refreshing...")
            qrcode_id, scan_url = await self._fetch_qr_code()
            self._print_qr_code(scan_url)
            continue
```

### 10.4 版本迁移
**提交**: `0dad6124a2f973e9efd0f32c73a0a388a76b35df`
**日期**: 2026-03-24
**作者**: xcosmosbox

```python
WEIXIN_CHANNEL_VERSION = "1.0.3"
BASE_INFO: dict[str, str] = {"channel_version": WEIXIN_CHANNEL_VERSION}
```

---

## 11. 重试机制重构

**提交**: `f0f0bf02d77e24046a4c35037d5bd3d938222bc7`
**日期**: 2026-03-25
**作者**: Xubin Ren

### 变更摘要
将重试策略集中在 ChannelManager 中，各个频道在发送失败时统一抛出异常。

### 关键代码变更

**nanobot/channels/base.py**
```python
async def send(self, msg: OutboundMessage) -> None:
    """Send a message to the channel.

    Implementations should raise on delivery failure so the channel manager
    can apply any retry policy in one place.
    """
    pass
```

**nanobot/channels/manager.py**
```python
@staticmethod
async def _send_once(channel: BaseChannel, msg: OutboundMessage) -> None:
    """Send one outbound message without retry policy."""
    if msg.metadata.get("_stream_delta") or msg.metadata.get("_stream_end"):
        await channel.send_delta(msg.chat_id, msg.content, msg.metadata)
    elif not msg.metadata.get("_streamed"):
        await channel.send(msg)
```

所有频道（Feishu, Mochat, Slack, Telegram, WeCom, WeiXin, WhatsApp）的发送方法现在在失败时都会抛出异常。

---

## 12. 文档更新

### 12.1 Discord 组策略说明
**提交**: `b7df3a0aea71abb266ccaf96813129dfd9598cf7`, `321214e2e0c03415b5d4c872890508b834329a7f`
**日期**: 2026-03-24
**作者**: Seeratul

```markdown
- If you set group policy to open create new threads as private threads and then @ the bot into it.
  Otherwise the thread itself and the channel in which you spawned it will spawn a bot session.
```

### 12.2 微信路由标签文档
**提交**: `0ccfcf6588420eaf485bd14892b2bf3ee1db4e78`
**日期**: 2026-03-24
**作者**: xcosmosbox

```markdown
> - `routeTag`: Optional. When your upstream Weixin deployment requires request routing,
>   nanobot will send it as the `SKRouteTag` header.
```

---

## 测试覆盖

本次提交周期新增了大量测试用例，涵盖：

1. **Telegram 频道测试**: 流式消息分段、未修改错误处理、缓冲区管理
2. **频道管理器测试**: 重试逻辑、取消错误传播、流式消息发送
3. **Cron 工具测试**: 时区默认行为、时间戳格式化
4. **Gemini thought_signature 测试**: 完整往返测试（SDK 对象、字典、流式）
5. **微信频道测试**: 会话暂停、QR 码刷新、状态持久化

---

## 总结

本周 NanoBot 的主要改进包括：

1. **稳定性增强**: 消息发送重试机制、微信会话管理优化
2. **功能扩展**: Step Fun 提供商支持、时区配置、Cron 时区继承
3. **兼容性改进**: OpenAI o1 支持、Gemini thought_signature 支持
4. **代码质量**: 重试机制重构、频道错误处理统一

所有变更均包含相应的测试覆盖，确保功能正确性和长期可维护性。
