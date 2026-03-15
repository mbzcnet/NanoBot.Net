# NanoBot Python 项目周报 (2026-03-08 ~ 2026-03-15)

## 概述

本周 NanoBot Python 项目共提交 **约 30+** 个 commit，主要集中在以下领域：

1. **新功能**: 交互式配置向导、Channel 插件架构
2. **功能增强**: DingTalk 文件/图片支持、Feishu 工具调用展示
3. **Bug 修复**: MCP 工具过滤语义、异常处理、DingTalk 文件保存路径
4. **重构**: 使用结构化评估替代 `<SILENT_OK>` 标记

---

## 关键更新详情

### 1. 交互式配置向导 (Onboard Wizard)

**Commit**: `e4c9115`  
**作者**: chengyongru  
**日期**: 2026-03-14

新增交互式 onboarding 向导，支持用户通过 CLI 逐步配置 LLM Provider 和 Channel。

**新增文件**:
- `nanobot/cli/onboard_wizard.py` (+697 行)

**核心代码片段** (使用 questionary 和 Rich 构建交互式 UI):

```python
def _get_field_type_info(field_info) -> tuple[str, Any]:
    """Extract field type info from Pydantic field."""
    annotation = field_info.annotation
    if annotation is None:
        return "str", None
    
    origin = get_origin(annotation)
    args = get_args(annotation)
    
    # Handle Optional[T] / T | None
    if origin is types.UnionType:
        non_none_args = [a for a in args if a is not type(None)]
        # ... type extraction logic
```

**CLI 集成**:

```python
# nanobot/cli/commands.py
@cli.command()
async def configure(ctx: Context, interactive: bool = False):
    """Configure nanobot settings."""
    if interactive:
        from nanobot.cli.onboard_wizard import run_onboard_wizard
        await run_onboard_wizard()
    else:
        # ... existing config logic
```

---

### 2. Channel 插件架构

**Commit**: `dbdb43f`  
**作者**: Xubin Ren  
**日期**: 2026-03-13

实现基于 Python entry_points 的插件化架构，支持动态发现 Channel。

**核心改动**:
- Channel Config 类从 `schema.py` 移至各自模块
- 新增 `nanobot/channels/base.py` 基类
- 新增 `nanobot/plugins list` CLI 命令
- 新增 `docs/CHANNEL_PLUGIN_GUIDE.md` 插件开发指南

**Config 类迁移示例** (从 schema.py 迁移到 channel 模块):

```python
# nanobot/channels/telegram.py
class TelegramConfig(BaseModel):
    """Telegram channel configuration."""
    bot_token: str
    api_id: Optional[str] = None
    api_hash: Optional[str] = None
    # ... fields
    
    @classmethod
    def default_config(cls) -> dict:
        """Return default config for onboard."""
        return {"bot_token": "", "allowed_chats": []}
```

**插件发现机制**:

```python
# nanobot/channels/registry.py
def get_channel_plugins() -> dict[str, ChannelPlugin]:
    """Discover channel plugins via entry_points."""
    plugins = {}
    for ep in importlib.metadata.entry_points(group="nanobot.channels"):
        # Load and register channel plugins
```

---

### 3. 替换 `<SILENT_OK>` 为结构化评估

**Commit**: `411b059`  
**作者**: Xubin Ren  
**日期**: 2026-03-14

使用轻量级 LLM 调用评估后台任务结果，决定是否通知用户。

**新增文件**: `nanobot/utils/evaluator.py` (+92 行)

**核心代码**:

```python
# nanobot/utils/evaluator.py
_EVALUATE_TOOL = [
    {
        "type": "function",
        "function": {
            "name": "evaluate_notification",
            "description": "Decide whether the user should be notified...",
            "parameters": {
                "type": "object",
                "properties": {
                    "should_notify": {
                        "type": "boolean",
                        "description": "true = result contains actionable info...",
                    },
                    "reason": {"type": "string"},
                },
                "required": ["should_notify"],
            },
        },
    }
]

async def evaluate_response(
    response: str,
    task_context: str,
    provider: LLMProvider,
    model: str,
) -> bool:
    """Decide whether a background-task result should be delivered."""
    # Lightweight LLM call to decide notification
```

---

### 4. MCP enabledTools 过滤语义修复

**Commit**: `a1241ee`  
**作者**: Xubin Ren  
**日期**: 2026-03-14

明确 MCP 工具过滤逻辑，支持原始和包装后的工具名。

**核心改动** (`nanobot/agent/tools/mcp.py`):

```python
def _filter_mcp_tools(tools: list, enabled_tools: list[str]) -> list:
    """Filter MCP tools based on enabled_tools config."""
    if not enabled_tools:
        return []
    if enabled_tools == ["*"]:
        return tools  # All tools enabled
    
    # Support both raw and wrapped tool names
    enabled = set(enabled_tools)
    for tool in tools:
        wrapped_name = f"mcp_{server}_{tool.name}"
        if wrapped_name in enabled or tool.name in enabled:
            filtered.append(tool)
    return filtered
```

---

### 5. DingTalk 文件/图片/富文本支持

**Commit**: `b15c3f8`  
**作者**: Meng Yuhang  
**日期**: 2026-03-12

新增 DingTalk 频道对文件、图片、富文本消息的接收支持。

**代码改动** (`nanobot/channels/dingtalk.py`):

```python
async def handle_event(self, event: dict) -> None:
    """Handle DingTalk events including file/image/richText."""
    msg_type = event.get("msgtype")
    
    if msg_type == "file":
        file_info = event.get("file")
        file_url = await self._download_file(file_info["media_id"])
        content = f"[File: {file_info['file_name']}]({file_url})"
        
    elif msg_type == "image":
        image_info = event.get("image")
        image_url = await self._download_file(image_info["media_id"])
        content = f"![Image]({image_url})"
        
    elif msg_type == "richText":
        # Handle rich text content
        content = self._parse_rich_text(event.get("rich_text"))
```

---

### 6. Feishu 工具调用展示优化

**Commit**: `19ae7a1`  
**日期**: 2026-03-14

修复 Feishu 工具提示格式和 think 标签 stripping 问题。

**代码改动** (`nanobot/channels/feishu.py`):

```python
def _strip_think_tags(self, content: str) -> str:
    """Remove <think>...</think> tags from content."""
    import re
    return re.sub(r'<think>.*?</think>', '', content, flags=re.DOTALL)

async def send_tool_hint(self, tool_names: list[str]) -> None:
    """Send tool hint card with formatted tool list."""
    # Use code block formatting for tool hints
    tool_list = ",\n".join(f"`{t}`" for t in tool_names)
    # ... avoid breaking formatting
```

---

### 7. Bug 修复汇总

| Commit | 描述 |
|--------|------|
| `2c10bd4` | 修复 DingTalk 下载文件保存到 `/tmp` 而非 media 目录 |
| `a2acacd` | 添加异常处理防止 agent loop 崩溃 |
| `61f0923` | Telegram 帮助文本包含 restart 命令 |

**异常处理代码** (`nanobot/agent/loop.py`):

```python
async def run_loop(self):
    """Main agent loop with error handling."""
    try:
        async for message in self.message_stream:
            await self.process_message(message)
    except Exception as e:
        logger.error(f"Agent loop crashed: {e}")
        # Graceful recovery instead of crash
        await self._recover()
```

---

## 统计信息

| 指标 | 数量 |
|------|------|
| 总 Commit | ~30+ |
| 新增文件 | 4 |
| 修改文件 | 25+ |
| 新增代码行 | ~1500+ |
| 测试用例 | +225 (新增插件测试) |

---

## 值得关注的 PR

- **PR #1966**: Feishu 工具调用代码块展示
- **PR #1963**: Feishu 消息回复/引用支持
- **PR #1981**: Wecom SDK 升级至 >=0.1.5

---

## 下周展望

1. 继续完善 onboard wizard
2. 可能有更多 channel 插件贡献
3. 可能的性能优化 (token-based context window 已在使用)

---

*报告生成时间: 2026-03-15*
