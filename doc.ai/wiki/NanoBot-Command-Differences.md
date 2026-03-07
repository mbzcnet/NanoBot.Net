# NanoBot 命令实现差异说明

本文档记录 NanoBot.NET (.NET 8 C# 实现) 与原项目 nanobot (Python 实现) 在命令实现方面的主要差异。

## 框架差异

| 方面 | Python 原项目 | .NET 实现 |
|------|---------------|-----------|
| CLI 框架 | `typer` + `prompt_toolkit` | `System.CommandLine` |
| 交互式输入 | prompt_toolkit (支持历史、粘贴、多平台) | `Console.ReadLine()` (简单实现) |
| 富文本输出 | `rich` 库 (表格、Markdown、颜色) | `Console` 原生 (基础颜色) |
| 日志系统 | `loguru` | NLog |

## 命令结构对比

### 共同命令

两个项目共有以下命令：

| 命令 | Python | .NET | 差异说明 |
|------|--------|------|----------|
| `onboard` | ✅ | ✅ | 功能基本一致，.NET 添加了 `--non-interactive` 和多 LLM profile 配置 |
| `agent` | ✅ | ✅ | .NET 添加了 `--streaming`、`--skip-check` 选项；交互模式实现简化 |
| `gateway` | ✅ | ✅ | 功能一致 |
| `status` | ✅ | ✅ | .NET 额外支持 `--json` 输出格式 |
| `channels` | ✅ | ✅ | 子命令：status, login；功能一致 |
| `cron` | ✅ | ✅ | 子命令：list, add, remove, enable, run；功能一致 |
| `provider` | ✅ | ✅ | 仅支持 login 子命令，OAuth 登录未完全实现 |

### .NET 独有命令

| 命令 | 说明 |
|------|------|
| `config` | 配置管理 (查看/编辑配置) |
| `session` | 会话管理 (列表、清除、导出) |
| `webui` | WebUI 启动管理 |
| `mcp` | MCP 服务器管理 |

### Python 独有功能

- **Mochat 渠道**: Python 支持，.NET 尚未实现
- **Matrix 渠道**: Python 支持，.NET 尚未实现

## 具体实现差异

### 1. Agent 命令

**Python 实现特点：**
- 使用 `prompt_toolkit` 实现交互式输入（支持命令历史、粘贴、多行模式）
- 通过 Rich 库渲染 Markdown 输出
- 支持 `--logs` 控制日志显示/隐藏
- 交互模式使用异步消息队列与 Agent 通信

**.NET 实现特点：**
- 使用简单的 `Console.ReadLine()` 实现交互输入
- 内置简单的 Markdown 解析器进行基础渲染
- 支持 `--streaming` 实时流式输出（Python 版本无此功能）
- 支持 `--skip-check` 跳过配置检查
- 单消息模式和交互模式实现更简洁

**选项对比：**

```bash
# Python
nanobot agent -m "Hello" --session cli:direct --markdown/--no-markdown --logs/--no-logs

# .NET
nbot agent -m "Hello" -s cli:direct --markdown --logs --skip-check --streaming/--no-streaming
```

### 2. Onboard 命令

**Python 实现：**
```python
@app.command()
def onboard():
    """Initialize nanobot configuration and workspace."""
    # 创建/更新配置
    # 创建工作空间
    # 同步模板文件
```

** .NET 实现：**
```csharp
// 额外支持：
// - --non-interactive 非交互模式
// - --provider, --model, --api-key, --api-base 命令行配置 LLM
// - 多 LLM Profile 配置 (Python 只有单一模型配置)
// - 内置 WebUI 默认配置
```

### 3. Provider Login

**Python 实现：**
- 使用 `oauth_cli_kit` 库实现 OAuth 交互登录
- 支持 OpenAI Codex 和 GitHub Copilot 的完整 OAuth 设备流

** .NET 实现：**
- OAuth 功能仅提供说明信息，未实现完整流程
- 建议用户使用环境变量或手动配置 API Key

```csharp
// .NET 输出示例：
// OpenAI Codex OAuth login requires the oauth-cli-kit package.
// In .NET, you can configure OpenAI Codex by:
//   1. Running: dotnet user-secrets set "OpenAI:ApiKey" "your-api-key"
//   2. Or setting environment variable: OPENAI_API_KEY
```

### 4. Cron 命令

功能实现基本一致，选项对比：

| 选项 | Python | .NET |
|------|--------|------|
| list --all | ✅ | ✅ |
| add --name, --message, --every/--cron/--at | ✅ | ✅ |
| add --deliver, --to, --channel | ✅ | ✅ |
| remove | ✅ | ✅ |
| enable/disable | ✅ | ✅ |
| run | ✅ (支持 --force) | ✅ (无 --force) |

### 5. Gateway 命令

**Python 实现：**
- 启动完整的服务栈（Agent、Cron、Heartbeat、Channels）
- 使用异步消息队列 (`MessageBus`) 处理消息

** .NET 实现：**
- 同样启动完整服务栈
- 使用 `System.Threading.Channels` 实现消息队列
- 配置加载方式略有不同（支持环境变量 `NBOT_` 前缀覆盖）

### 6. Status 命令

**.NET 额外功能：**
```bash
nbot status --json  # 输出 JSON 格式
```

Python 版本仅支持文本输出。

### 7. Session 管理

**.NET 新增命令：**
```bash
nbot session --list           # 列出所有会话
nbot session --clear <id>     # 清除指定会话
nbot session --clear-all      # 清除所有会话
nbot session --export <id>    # 导出会话到文件
```

Python 版本无独立 session 命令，会话通过 workspace 文件系统管理。

### 8. Config 命令

**.NET 独有命令：**
```bash
nbot config --list     # 列出配置项
nbot config --get key  # 获取单个配置值
nbot config --set key=value  # 设置配置值
```

### 9. WebUI 命令

**.NET 独有命令：**
```bash
nbot webui              # 启动 WebUI
nbot webui --host x     # 指定主机
nbot webui --port x     # 指定端口
nbot webui --token x    # 指定认证 Token
```

### 10. MCP 命令

**.NET 独有命令：**
```bash
nbot mcp                # 列出已配置的 MCP 服务器
nbot mcp start <name>  # 启动指定的 MCP 服务器
nbot mcp stop <name>   # 停止指定的 MCP 服务器
```

## 输出格式差异

### Logo 显示

- Python: `__logo__` = 🐈\n
- .NET: `🐈` (直接使用 Emoji)

### 响应输出

**Python (Rich 库):**
```python
console.print(f"[cyan]{__logo__} nanobot[/cyan]")
console.print(Markdown(content))
```

**.NET (自定义解析):**
```csharp
Console.WriteLine("🐈 NBot：");
PrintMarkdown(response);
```

## 配置系统差异

| 方面 | Python | .NET |
|------|--------|------|
| 配置文件 | `~/.nanobot/config.json` | `~/.nbot/config.json` |
| 配置覆盖 | 环境变量 | 环境变量 + `NBOT_` 前缀 |
| LLM 配置 | `agents.defaults` | `Llm.Profiles` (多 Profile) |
| Channel 配置 | `channels.*` | `Channels.*` (结构一致) |
| WebUI 配置 | 无 | `WebUI.*` (完整配置) |

## 总结

NanoBot.NET 在保持与原项目核心功能一致的同时，有以下主要变化：

1. **框架转换**: 使用 .NET 生态系统 (System.CommandLine, NLog)
2. **扩展功能**: 新增 WebUI、MCP、Config、Session 管理命令
3. **简化实现**: 交互模式使用更简单的 Console.ReadLine()
4. **多 Profile**: LLM 配置支持多个 Profile（用于不同场景）
5. **OAuth 简化**: Provider login 仅提供说明，未实现完整 OAuth 流程

这些差异使得 NanoBot.NET 更适合 .NET 生态系统用户，同时保持了与原项目相似的使用体验。