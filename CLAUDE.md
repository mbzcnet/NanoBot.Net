# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
dotnet build                    # Build entire solution
dotnet test                     # Run all tests
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"  # Run a single test
dotnet test tests/NanoBot.Agent.Tests   # Run one test project
dotnet run --project src/NanoBot.Cli -- agent   # Run the CLI
dotnet pack src/NanoBot.Cli/NanoBot.Cli.csproj -c Release  # Pack for NuGet
```

## Architecture

NanoBot.Net is a .NET 8 port of [nanobot](https://github.com/HKUDS/nanobot) — an ultra-lightweight personal AI assistant built on **Microsoft.Agents.AI** framework.

### Project Dependency Flow

```
NanoBot.Cli → NanoBot.Agent → NanoBot.Core (abstractions/interfaces)
            → NanoBot.Channels       ↑
            → NanoBot.Tools          ↑
            → NanoBot.Providers      ↑
            → NanoBot.Infrastructure ↑
```

### Key Layers

- **Core** — Interfaces and models only: `IAgentRuntime`, `IMessageBus`, `IWorkspaceManager`, `IMemoryStore`, `ISessionManager`, `ISkillsLoader`, `IChannel`, `ICronService`, `IHeartbeatService`, `ISubagentManager`. All config models live here (`AgentConfig` → `LlmConfig`, `WorkspaceConfig`, `MemoryConfig`, `ChannelsConfig`, etc.).
- **Agent** — The agent loop. `AgentRuntime` orchestrates message processing. `NanoBotAgentFactory` creates `ChatClientAgent` instances wired with tools, context providers, and chat history providers. Context is injected dynamically via `AIContextProvider` implementations (Bootstrap, Skills, Memory, MemoryConsolidation). Chat history is managed by `ChatHistoryProvider` chain (Composite → MemoryConsolidation → FileBacked).
- **Providers** — `ChatClientFactory` creates `IChatClient` for 10+ LLM backends (OpenAI, Anthropic, OpenRouter, DeepSeek, Groq, Gemini, Ollama, custom). Includes middleware clients: `SanitizingChatClient` (cleans responses), `InterimTextRetryChatClient` (retries on malformed think tags).
- **Tools** — `ToolProvider.CreateDefaultToolsAsync()` builds `AITool`/`AIFunction` instances. Includes File, Shell, Web, Message, Cron, Spawn tools + MCP (Model Context Protocol) server integration.
- **Channels** — Channel implementations (Telegram, Discord, Feishu, Slack, Email, etc.) that connect to the message bus.
- **Infrastructure** — `MessageBus` (bounded `System.Threading.Channels` with inbound/outbound queues), `WorkspaceManager` (file system abstraction with embedded resource bootstrap), `MemoryStore`, `CronService`, `HeartbeatService`, `SkillsLoader`, `SubagentManager`.
- **Cli** — Entry point using `System.CommandLine`. Commands: agent, configure, gateway, session, cron, mcp, status.

### Core Patterns

- **DI everywhere** — Service registration via `ServiceCollectionExtensions` in Agent, Cli, and Infrastructure projects. Most services are singletons.
- **Message bus** — Channels push to inbound queue; `AgentRuntime` consumes, processes via the agent, and pushes responses to outbound queue; dispatcher routes outbound messages back to the originating channel.
- **Configuration cascade** — `--config` CLI flag → `config.json` → `agent.json` → `~/.nbot/config.json`. Environment variables with `NBOT_` prefix override config values.
- **Workspace** — `IWorkspaceManager` manages paths for memory (MEMORY.md, HISTORY.md), skills, sessions, and persona files (agents.md, soul.md, user.md, tools.md, heartbeat.md). Bootstrap files are embedded resources.
- **Memory** — `IMemoryStore` persists to MEMORY.md/HISTORY.md. `MemoryConsolidator` summarizes history. Default memory window is 50 messages.
- **Sessions** — Keyed by `channel:chatId`, stored as files in workspace/sessions.

## Tech Stack

- .NET 8, C# 12, nullable enabled
- Microsoft.Agents.AI 1.0.0-rc1 (ChatClientAgent, AITool, AIContextProvider, ChatHistoryProvider)
- Microsoft.Extensions.AI (IChatClient)
- xUnit 2.9.2 + Moq 4.20.72 for tests
- System.CommandLine for CLI
- Version managed centrally in `Directory.Build.props` (currently 0.1.2)
