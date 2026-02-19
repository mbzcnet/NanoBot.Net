# NanoBot.Net

<div align="center">
  <h1>NanoBot.Net: Ultra-Lightweight Personal AI Assistant for .NET</h1>
  <p>
    <img src="https://img.shields.io/badge/.NET-8.0+-512BD4" alt=".NET">
    <img src="https://img.shields.io/badge/C%23-12.0-239120" alt="C#">
    <img src="https://img.shields.io/badge/license-MIT-green" alt="License">
    <img src="https://img.shields.io/badge/platforms-Windows%20%7C%20macOS%20%7C%20Linux-blue" alt="Platforms">
  </p>
</div>

**NanoBot.Net** is a .NET port of the excellent [nanobot](https://github.com/HKUDS/nanobot) — an ultra-lightweight personal AI assistant.

⚡️ Delivers core agent functionality in **~12,000 lines** of C# code, re-implemented with necessary .NET optimizations and extensions.

🏗️ Built on **Microsoft.Agents.AI** framework — leveraging the official .NET Agent framework for optimal compatibility and extensibility.

## Key Features

🪶 **Lightweight**: ~12,000 lines of clean C# code — minimal footprint, maximum efficiency.

🔬 **Research-Ready**: Clean, readable C# code that's easy to understand, modify, and extend.

⚡️ **Lightning Fast**: Native .NET performance with minimal startup time and resource usage.

💎 **Easy-to-Use**: One-click deployment with simple configuration.

🔌 **Framework-Based**: Built on Microsoft.Agents.AI for seamless integration with .NET ecosystem.

## Features

- **Multiple LLM Providers**: Support for OpenAI, Azure OpenAI, Anthropic, OpenRouter, DeepSeek, Groq, Gemini, Ollama, and custom OpenAI-compatible endpoints
- **Multiple Channels**: Discord, Slack, Telegram, WhatsApp, DingTalk, Feishu, QQ, Email, and more
- **Extensible Tools**: File operations, shell commands, web search, MCP (Model Context Protocol) support
- **Memory & Context**: Persistent memory with context injection
- **Session Management**: Multi-session support with history
- **Scheduled Tasks**: Cron-based job scheduling
- **Heartbeat Service**: Periodic autonomous actions
- **Skills System**: Loadable skill modules for extended capabilities

## Installation

### Homebrew (macOS / Linux / WSL)

```bash
brew tap mbzcnet/tap
brew install nbot
nbot --version
```

### Windows (PowerShell)

```powershell
irm https://raw.githubusercontent.com/NanoBot/NanoBot.Net/main/install/install.ps1 | iex
nbot --version
```

### curl (macOS / Linux / WSL)

```bash
curl -fsSL https://raw.githubusercontent.com/NanoBot/NanoBot.Net/main/install/install.sh | bash
nbot --version
```

### dotnet tool (All Platforms)

```bash
dotnet tool install --global NanoBot.Cli
nbot --version
```

### Build from Source

```bash
git clone https://github.com/NanoBot/NanoBot.Net.git
cd NanoBot.Net
dotnet build -c Release
dotnet run --project src/NanoBot.Cli -- --version
```

## Quick Start

### 1. Initialize Workspace

```bash
nbot onboard
```

This creates a `~/.nbot` directory with default configuration files.

### 2. Configure LLM Provider

Edit `~/.nbot/config.json`:

```json
{
  "Agent": {
    "Llm": {
      "Provider": "openai",
      "Model": "gpt-4o",
      "ApiKey": "your-api-key-here"
    }
  }
}
```

Or use environment variable:

```bash
export NBOT_AGENT_LLM_APIKEY="your-api-key-here"
```

### 3. Start Chatting

```bash
nbot agent
```

Or send a single message:

```bash
nbot agent -m "Hello, what can you do?"
```

## Commands

| Command | Description |
|---------|-------------|
| `nbot onboard` | Initialize workspace |
| `nbot agent` | Start interactive chat |
| `nbot agent -m "..."` | Send a single message |
| `nbot gateway` | Start gateway service mode |
| `nbot status` | Show agent status |
| `nbot config` | Manage configuration |
| `nbot session` | Manage sessions |
| `nbot cron` | Manage scheduled tasks |
| `nbot mcp` | Manage MCP servers |

Interactive mode exits: `exit`, `quit`, `/exit`, `/quit`, `:q`, or `Ctrl+D`.

## Configuration

Configuration is loaded from (in order):
1. `--config` command line option
2. `config.json` in current directory
3. `agent.json` in current directory
4. `~/.nbot/config.json`

Environment variables are supported with `NBOT_` prefix:
- `NBOT_AGENT_LLM_APIKEY`
- `NBOT_AGENT_LLM_MODEL`
- etc.

### Providers

| Provider | Purpose | Get API Key |
|----------|---------|-------------|
| `custom` | Any OpenAI-compatible endpoint | — |
| `openrouter` | LLM (recommended, access to all models) | [openrouter.ai](https://openrouter.ai) |
| `anthropic` | LLM (Claude direct) | [console.anthropic.com](https://console.anthropic.com) |
| `openai` | LLM (GPT direct) | [platform.openai.com](https://platform.openai.com) |
| `deepseek` | LLM (DeepSeek direct) | [platform.deepseek.com](https://platform.deepseek.com) |
| `groq` | LLM + Voice transcription (Whisper) | [console.groq.com](https://console.groq.com) |
| `gemini` | LLM (Gemini direct) | [aistudio.google.com](https://aistudio.google.com) |
| `ollama` | LLM (local) | — |

<details>
<summary>OpenRouter Configuration (Recommended)</summary>

```json
{
  "Agent": {
    "Llm": {
      "Provider": "openrouter",
      "Model": "anthropic/claude-opus-4-5",
      "ApiKey": "sk-or-v1-xxx"
    }
  }
}
```

</details>

<details>
<summary>Custom Provider (Any OpenAI-compatible API)</summary>

```json
{
  "Agent": {
    "Llm": {
      "Provider": "custom",
      "Model": "your-model-name",
      "ApiKey": "your-api-key",
      "ApiBase": "https://api.your-provider.com/v1"
    }
  }
}
```

For local servers that don't require a key, set `ApiKey` to any non-empty string.

</details>

<details>
<summary>Ollama (Local LLM)</summary>

```json
{
  "Agent": {
    "Llm": {
      "Provider": "ollama",
      "Model": "llama3.1",
      "ApiBase": "http://localhost:11434/v1"
    }
  }
}
```

</details>

### MCP (Model Context Protocol)

NanoBot.Net supports [MCP](https://modelcontextprotocol.io/) — connect external tool servers and use them as native agent tools.

Add MCP servers to your `config.json`:

```json
{
  "Tools": {
    "McpServers": {
      "filesystem": {
        "Command": "npx",
        "Args": ["-y", "@modelcontextprotocol/server-filesystem", "/path/to/dir"]
      }
    }
  }
}
```

Two transport modes are supported:

| Mode | Config | Example |
|------|--------|---------|
| **Stdio** | `Command` + `Args` | Local process via `npx` / `uvx` |
| **HTTP** | `Url` | Remote endpoint (`https://mcp.example.com/sse`) |

## Chat Apps

Connect NanoBot.Net to your favorite chat platform.

| Channel | What you need |
|---------|---------------|
| **Telegram** | Bot token from @BotFather |
| **Discord** | Bot token + Message Content intent |
| **WhatsApp** | QR code scan |
| **Feishu** | App ID + App Secret |
| **DingTalk** | App Key + App Secret |
| **Slack** | Bot token + App-Level token |
| **Email** | IMAP/SMTP credentials |
| **QQ** | App ID + App Secret |

<details>
<summary><b>Telegram</b> (Recommended)</summary>

**1. Create a bot**
- Open Telegram, search `@BotFather`
- Send `/newbot`, follow prompts
- Copy the token

**2. Configure**

```json
{
  "Channels": {
    "Telegram": {
      "Enabled": true,
      "Token": "YOUR_BOT_TOKEN",
      "AllowFrom": ["YOUR_USER_ID"]
    }
  }
}
```

**3. Run**

```bash
nbot gateway
```

</details>

<details>
<summary><b>Discord</b></summary>

**1. Create a bot**
- Go to https://discord.com/developers/applications
- Create an application → Bot → Add Bot
- Copy the bot token

**2. Enable intents**
- In the Bot settings, enable **MESSAGE CONTENT INTENT**

**3. Configure**

```json
{
  "Channels": {
    "Discord": {
      "Enabled": true,
      "Token": "YOUR_BOT_TOKEN",
      "AllowFrom": ["YOUR_USER_ID"]
    }
  }
}
```

**4. Run**

```bash
nbot gateway
```

</details>

<details>
<summary><b>Feishu (飞书)</b></summary>

Uses WebSocket long connection — no public IP required.

**1. Create a Feishu bot**
- Visit [Feishu Open Platform](https://open.feishu.cn/app)
- Create a new app → Enable **Bot** capability
- Get **App ID** and **App Secret**

**2. Configure**

```json
{
  "Channels": {
    "Feishu": {
      "Enabled": true,
      "AppId": "cli_xxx",
      "AppSecret": "xxx",
      "AllowFrom": []
    }
  }
}
```

**3. Run**

```bash
nbot gateway
```

</details>

<details>
<summary><b>Slack</b></summary>

Uses Socket Mode — no public URL required.

**1. Create a Slack app**
- Go to [Slack API](https://api.slack.com/apps) → **Create New App**

**2. Configure**
- **Socket Mode**: Toggle ON → Generate App-Level Token
- **OAuth & Permissions**: Add bot scopes: `chat:write`, `app_mentions:read`
- **Event Subscriptions**: Subscribe to `message.im`, `app_mention`

**3. Configure NanoBot.Net**

```json
{
  "Channels": {
    "Slack": {
      "Enabled": true,
      "BotToken": "xoxb-...",
      "AppToken": "xapp-...",
      "GroupPolicy": "mention"
    }
  }
}
```

**4. Run**

```bash
nbot gateway
```

</details>

<details>
<summary><b>Email</b></summary>

Give NanoBot.Net its own email account. It polls IMAP for incoming mail and replies via SMTP.

**1. Configure**

```json
{
  "Channels": {
    "Email": {
      "Enabled": true,
      "ConsentGranted": true,
      "ImapHost": "imap.gmail.com",
      "ImapPort": 993,
      "ImapUsername": "my-nbot@gmail.com",
      "ImapPassword": "your-app-password",
      "SmtpHost": "smtp.gmail.com",
      "SmtpPort": 587,
      "SmtpUsername": "my-nbot@gmail.com",
      "SmtpPassword": "your-app-password",
      "FromAddress": "my-nbot@gmail.com",
      "AllowFrom": ["your-email@gmail.com"]
    }
  }
}
```

**2. Run**

```bash
nbot gateway
```

</details>

## Security

| Option | Default | Description |
|--------|---------|-------------|
| `Tools.RestrictToWorkspace` | `false` | When `true`, restricts all agent tools to workspace directory |
| `Channels.*.AllowFrom` | `[]` | Whitelist of user IDs. Empty = allow everyone |

## Docker

### Docker Compose

```bash
docker compose run --rm nbot-cli onboard
vim ~/.nbot/config.json
docker compose up -d nbot-gateway
```

### Docker

```bash
docker build -t nbot .
docker run -v ~/.nbot:/root/.nbot --rm nbot onboard
vim ~/.nbot/config.json
docker run -v ~/.nbot:/root/.nbot nbot gateway
```

## Project Structure

```
src/
├── NanoBot.Core/           # Core abstractions (interfaces, models)
├── NanoBot.Infrastructure/ # Infrastructure implementations
├── NanoBot.Agent/          # Agent core implementation
├── NanoBot.Providers/      # LLM provider implementations
├── NanoBot.Tools/          # Tool implementations
├── NanoBot.Channels/       # Channel implementations
└── NanoBot.Cli/            # CLI entry point
```

### Code Statistics

| Module | Lines of Code | Description |
|--------|---------------|-------------|
| NanoBot.Infrastructure | ~2,934 | Workspace, Bus, Cron, Memory, Skills |
| NanoBot.Channels | ~2,681 | Telegram, Discord, Slack, Feishu, etc. |
| NanoBot.Cli | ~2,304 | Command-line interface |
| NanoBot.Agent | ~1,306 | Agent runtime, context providers |
| NanoBot.Core | ~1,260 | Core interfaces and models |
| NanoBot.Tools | ~1,188 | File, Shell, Web, MCP tools |
| NanoBot.Providers | ~162 | LLM provider factory |
| **Total** | **~11,835** | |

> Run `find src -name "*.cs" -type f ! -path "*/obj/*" | xargs wc -l` to verify.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Application Layer                       │
│                    (CLI / Gateway Service)                   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      Channel Layer                           │
│        (Telegram / Discord / Feishu / Slack / Email)         │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Message Bus Layer                         │
│                  (InboundQueue + OutboundQueue)              │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                     Agent Core Layer                         │
│          (Agent Loop / Context / Memory / Session)           │
└─────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│   Tool Layer    │ │ Provider Layer  │ │ Infrastructure  │
│ (File/Shell/Web)│ │ (OpenAI/etc.)   │ │ (Cron/Heartbeat)│
└─────────────────┘ └─────────────────┘ └─────────────────┘
```

## Technology Stack

| Dimension | Choice |
|-----------|--------|
| **Language/Runtime** | C# / .NET 8+ (LTS) |
| **Core Framework** | Microsoft.Agents.AI |
| **LLM Client** | Microsoft.Agents.AI (ChatClientAgent) - Built-in multimodal support |
| **Tool System** | AITool / AIFunction |
| **Dependency Injection** | Microsoft.Extensions.DependencyInjection |
| **Configuration** | Microsoft.Extensions.Configuration |
| **Logging** | Microsoft.Extensions.Logging |
| **JSON** | System.Text.Json |
| **CLI Framework** | System.CommandLine |

## Development

### Prerequisites

- .NET 8.0 SDK or later

### Build

```bash
dotnet build
```

### Test

```bash
dotnet test
```

### Pack

```bash
dotnet pack src/NanoBot.Cli/NanoBot.Cli.csproj -c Release
```

## Roadmap

- [ ] Multi-modal support (images, voice, video)
- [ ] Long-term memory enhancements
- [ ] Better reasoning (multi-step planning)
- [ ] More integrations (Calendar, etc.)
- [ ] Self-improvement mechanisms

## Contributing

PRs welcome! The codebase is intentionally small and readable.

## References

- [nanobot (Original Project)](https://github.com/HKUDS/nanobot)
- [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/)
- [Microsoft.Agents.AI GitHub](https://github.com/microsoft/agent-framework)

## License

MIT License

---

<p align="center">
  <sub>NanoBot.Net is for educational, research, and technical exchange purposes only</sub>
</p>
