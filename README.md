# NanoBot.Net

<div align="center">
  <h1>NanoBot.Net: Ultra-Lightweight Personal AI Assistant for .NET</h1>
  <p>
    <img src="https://img.shields.io/badge/.NET-10.0-512BD4" alt=".NET">
    <img src="https://img.shields.io/badge/C%23-12.0-239120" alt="C#">
    <img src="https://img.shields.io/badge/license-MIT-green" alt="License">
    <img src="https://img.shields.io/badge/platforms-Windows%20%7C%20macOS%20%7C%20Linux-blue" alt="Platforms">
  </p>
</div>

**NanoBot.Net** is a .NET port of the excellent [nanobot](https://github.com/HKUDS/nanobot) — an ultra-lightweight personal AI assistant.

⚡️ Delivers core agent functionality with clean C# code, re-implemented with necessary .NET optimizations and extensions.

🏗️ Built on **Microsoft.Agents.AI** framework — leveraging the official .NET Agent framework for optimal compatibility and extensibility.

## Key Features

🪶 **Lightweight**: Clean C# code — minimal footprint, maximum efficiency.

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
- **Vision / Multi-modal**: Image input support in CLI and WebUI
- **Feishu File Download**: Automatic download of images, audio, files, and media from Feishu messages
- **Tool Hints**: Real-time display of tool call progress during agent execution

## Installation

### Homebrew (macOS / Linux / WSL)

```bash
brew tap mbzcnet/tap
brew install nbot
nbot --version
```

### Windows (PowerShell)

```powershell
irm https://raw.githubusercontent.com/mbzcnet/NanoBot.Net/main/install/install.ps1 | iex
nbot --version
```

### curl (macOS / Linux / WSL)

```bash
curl -fsSL https://raw.githubusercontent.com/mbzcnet/NanoBot.Net/main/install/install.sh | bash
nbot --version
```

### dotnet tool (All Platforms)

```bash
dotnet tool install --global NanoBot.Cli
nbot --version
```

### Build from Source

```bash
git clone https://github.com/mbzcnet/NanoBot.Net.git
cd NanoBot.Net
dotnet build -c Release
dotnet run --project src/NanoBot.Cli -- --version
```

## Quick Start

### 1. Run Configuration Wizard

```bash
nbot config -i
```

This launches an interactive wizard to set up your LLM provider and API key.

For non-interactive setup, use `nbot config --set`:

```bash
nbot config --set llm.profiles.default.provider=openai
nbot config --set llm.profiles.default.apikey=YOUR_API_KEY
```

### 2. Start Chatting

```bash
nbot agent
```

Or send a single message:

```bash
nbot agent -m "Hello, what can you do?"
```

> **Note**: If configuration is incomplete, the agent will prompt you to run `nbot config -i` first.

### 3. Launch the WebUI

```bash
nbot web
```

This command boots the Blazor-based WebUI. Useful options:

- `-p, --port <number>`: change the listening port (default `18888`).
- `-c, --config <path>`: specify a custom configuration file (defaults to `~/.nbot/config.json`).
- `--no-browser`: skip automatically opening the browser (helpful on headless servers).

If you prefer running directly from source without installing the global tool:

```bash
dotnet run --project src/NanoBot.Cli -- web --port 18888
```

## Commands

| Command | Description |
|---------|-------------|
| `nbot onboard` | Initialize workspace |
| `nbot agent` | Start interactive chat |
| `nbot agent -m "..."` | Send a single message |
| `nbot web` | Start the WebUI server |
| `nbot gateway` | Start gateway service mode |
| `nbot status` | Show agent status |
| `nbot config` | Manage configuration |
| `nbot session` | Manage sessions |
| `nbot cron` | Manage scheduled tasks |
| `nbot mcp` | Manage MCP servers |
| `nbot channels` | Manage channels |
| `nbot provider` | Manage OAuth providers |
| `nbot benchmark` | Run LLM model availability benchmark |

### Image / Vision Support

NanoBot.Net supports multi-modal image input in CLI. Use Markdown image syntax to reference images:

```bash
# Using relative path (workspace root or sessions directory)
nbot agent -m "Describe this image: ![alt](image.jpg)"

# Using relative path to sessions folder
nbot agent -m "What is in this image? ![photo](sessions/myimage.png)"

# Using absolute path
nbot agent -m "Analyze this: ![screenshot](/path/to/image.jpg)"
```

**Supported formats**: PNG, JPEG, GIF, WebP, BMP, SVG

**Path resolution order**:
1. Absolute paths (if file exists)
2. Sessions directory (`~/.bot/workspace/sessions/`)
3. Workspace root (`~/.bot/workspace/`)

For interactive mode, simply paste the image reference in your message.

Interactive mode exits: `exit`, `quit`, `/exit`, `/quit`, `:q`, or `Ctrl+D`.

## Configuration

### Configuration Wizard

The easiest way to configure NanoBot.Net is using the interactive wizard:

```bash
nbot config -i
```

This will guide you through:
1. **LLM Provider Selection** - Choose from OpenAI, Anthropic, OpenRouter, DeepSeek, Moonshot, Zhipu, or Ollama
2. **Model Selection** - Defaults are provided for each provider
3. **API Key** - Secure input with masking (or use environment variables)
4. **Workspace Path** - Where to store sessions and memory

For CI/CD or scripts, set values directly:

```bash
# Set LLM profile
nbot config --set llm.profiles.default.provider=openai
nbot config --set llm.profiles.default.model=gpt-4o-mini
nbot config --set llm.profiles.default.apikey=YOUR_KEY

# Or view current config
nbot config --list
```

### Configuration Loading

Configuration is loaded from (in order):
1. `--config` command line option
4. `~/.nbot/config.json`

Environment variables are supported with `NBOT_` prefix:
- `NBOT_AGENT_LLM_APIKEY`
- `NBOT_AGENT_LLM_MODEL`
- etc.

### Multiple LLM Profiles

NanoBot.Net supports multiple LLM profiles, allowing you to switch between different providers or models easily.

```json
{
  "Llm": {
    "DefaultProfile": "default",
    "Profiles": {
      "default": {
        "Provider": "openai",
        "Model": "gpt-4o-mini",
        "ApiKey": "${OPENAI_API_KEY}",
        "ApiBase": "https://api.openai.com/v1"
      },
      "claude": {
        "Provider": "anthropic",
        "Model": "claude-3-5-sonnet-20241022",
        "ApiKey": "${ANTHROPIC_API_KEY}",
        "ApiBase": "https://api.anthropic.com/v1"
      },
      "ollama": {
        "Provider": "ollama",
        "Model": "llama3.2",
        "ApiBase": "http://localhost:11434/v1"
      }
    }
  }
}
```

#### Managing Profiles via CLI

```bash
# View all profiles
nbot config --list

# Get specific profile field
nbot config --get llm.profiles.default.model

# Set profile field
nbot config --set llm.profiles.claude.provider=anthropic

# Change default profile
nbot config --set llm.defaultprofile=claude
```

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
  "Llm": {
    "DefaultProfile": "default",
    "Profiles": {
      "default": {
        "Provider": "openrouter",
        "Model": "anthropic/claude-opus-4-5",
        "ApiKey": "sk-or-v1-xxx"
      }
    }
  }
}
```

</details>

<details>
<summary>Custom Provider (Any OpenAI-compatible API)</summary>

```json
{
  "Llm": {
    "DefaultProfile": "default",
    "Profiles": {
      "default": {
        "Provider": "openai",
        "Model": "your-model-name",
        "ApiKey": "your-api-key",
        "ApiBase": "https://api.your-provider.com/v1"
      }
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
  "Llm": {
    "DefaultProfile": "default",
    "Profiles": {
      "default": {
        "Provider": "ollama",
        "Model": "llama3.1",
        "ApiBase": "http://localhost:11434/v1"
      }
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

**Features:**
- **File Download**: Automatically downloads images, audio files, documents, and media from Feishu messages
- **File Storage**: Saves downloaded files to `~/.nanobot/media/` directory
- **Supported Formats**: PNG, JPG, JPEG, GIF, BMP, WebP, ICO, TIFF, OPUS audio, MP4 video, PDF, Office documents, etc.

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

## New Features (Latest Release)

### Feishu File Download

NanoBot.Net automatically downloads and saves files from Feishu messages, making them available for agent processing.

**Supported File Types:**
- **Images**: PNG, JPG, JPEG, GIF, BMP, WebP, ICO, TIFF
- **Audio**: OPUS format
- **Documents**: PDF, Word (DOC/DOCX), Excel (XLS/XLSX), PowerPoint (PPT/PPTX)
- **Media**: MP4 videos and other media files

**How it Works:**
1. When a Feishu message contains files, NanoBot.Net automatically detects them
2. Downloads files using Feishu's API endpoints
3. Saves files to the local `~/.nanobot/media/` directory
4. Makes file paths available in the message context for agent processing

**Configuration:** No additional configuration required - works automatically with existing Feishu setup.

### Tool Hints

During agent execution, NanoBot.Net provides real-time feedback about tool calls being executed.

**How it Works:**
1. When the agent decides to use tools, it displays a concise hint like `web_search("query")`
2. Shows progress during tool execution
3. Provides immediate feedback about what the agent is currently doing

**Example:**
```
Agent: I'm searching for information about AI assistants...
web_search("AI assistants comparison")
Agent: Found several relevant results. Based on the information...
```

**Benefits:**
- **Transparency**: Users can see what the agent is doing in real-time
- **Better UX**: Provides feedback during potentially long-running operations
- **Debugging**: Helps identify when tools are being called and with what parameters

This feature works across all channels and provides a better user experience by showing the agent's thought process and tool usage.

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

- [x] **Feishu File Download**: Automatic download of images, audio, files, and media from Feishu messages ✅
- [x] **Tool Hints**: Real-time display of tool call progress during agent execution ✅
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
