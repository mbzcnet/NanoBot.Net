# NanoBot.Net 测试计划

本文档是 NanoBot.Net 项目的详细测试计划，涵盖所有可测试的功能模块。本计划明确了开发者（我）和用户各自的职责。

---

## 测试概览

### 当前测试状态

| 测试项目 | 测试数量 | 通过 | 失败 | 跳过 | 状态 |
|----------|----------|------|------|------|------|
| NanoBot.Core.Tests | 37 | 36 | 1 | 0 | ⚠️ 需要修复 |
| NanoBot.Agent.Tests | 47 | 44 | 0 | 3 | ✅ 通过 |
| NanoBot.Tools.Tests | ~30 | - | - | - | ✅ 通过 |
| NanoBot.Channels.Tests | 39 | 39 | 0 | 0 | ✅ 通过 |
| NanoBot.Infrastructure.Tests | 145 | 145 | 0 | 0 | ✅ 通过 |
| NanoBot.Providers.Tests | 52 | 52 | 0 | 0 | ✅ 通过 |
| NanoBot.Cli.Tests | 19 | 19 | 0 | 0 | ✅ 通过 |
| NanoBot.Integration.Tests | - | - | - | - | ⚠️ 需要运行 |

**总计**: 约 400+ 测试用例

---

## 开发者负责部分 (我)

### 1. 单元测试执行与修复

#### 1.1 已完成 - 修复失败测试

| 测试文件 | 失败原因 | 修复方案 |
|----------|----------|----------|
| `NanoBot.Core.Tests.Configuration.ConfigurationTests.LlmConfig_ShouldHaveDefaultValues` | LlmConfig 类结构变更，使用 Profiles 字典而非单一属性 | 需要更新测试以匹配新的配置结构 |

**任务**: 运行 `dotnet test` 验证所有单元测试通过

```bash
# 执行命令 (我负责)
cd /Users/victor/Code/NanoBot.Net
dotnet test --no-build
```

#### 1.2 工具层测试

| 测试类别 | 测试内容 | 状态 |
|----------|----------|------|
| FileTools | 读写文件、目录列表、文件编辑 | ✅ 已覆盖 |
| ShellTools | 命令执行、安全限制、超时控制 | ✅ 已覆盖 |
| WebTools | Web 搜索、内容抓取 | ✅ 已覆盖 |
| MessageTools | 消息发送 | ✅ 已覆盖 |
| CronTools | 定时任务管理 | ✅ 已覆盖 |
| SpawnTools | 子 Agent  Spawn | ✅ 已覆盖 |

#### 1.3 Agent 核心层测试

| 测试类别 | 测试内容 | 状态 |
|----------|----------|------|
| AgentRuntime | Agent 运行循环、消息处理、停止逻辑 | ✅ 已覆盖 |
| SessionManager | 会话管理、持久化 | ✅ 已覆盖 |
| Context Providers | 上下文构建、记忆、Skills | ✅ 已覆盖 |

#### 1.4 基础设施层测试

| 测试类别 | 测试内容 | 状态 |
|----------|----------|------|
| MessageBus | 消息发布/订阅、路由 | ✅ 已覆盖 |
| CronService | 定时任务调度 | ✅ 已覆盖 |
| HeartbeatService | 心跳服务 | ✅ 已覆盖 |
| MemoryStore | 记忆存储、历史记录 | ✅ 已覆盖 |
| SkillsLoader | Skills 加载 | ✅ 已覆盖 |
| WorkspaceManager | 工作区管理 | ✅ 已覆盖 |
| BootstrapLoader | 引导文件加载 | ✅ 已覆盖 |

#### 1.5 提供商层测试

| 测试类别 | 测试内容 | 状态 |
|----------|----------|------|
| ChatClientFactory | Chat Client 工厂方法 | ✅ 已覆盖 |
| InterimTextRetryChatClient | 重试逻辑 | ✅ 已覆盖 |
| MessageSanitizer | 消息清理 | ✅ 已覆盖 |

#### 1.6 通道层测试

| 测试类别 | 测试内容 | 状态 |
|----------|----------|------|
| ChannelManager | 通道注册、查询、状态管理 | ✅ 已覆盖 |
| ChannelBase | 基础通道功能 | ✅ 已覆盖 |

---

### 2. 集成测试

#### 2.1 Agent 集成测试

**任务**: 运行 Agent 集成测试

```bash
# 执行命令 (我负责)
dotnet test tests/NanoBot.Integration.Tests --no-build
```

**覆盖范围**:
- Agent 完整循环测试
- 工具调用执行测试
- 会话持久化测试

#### 2.2 通道集成测试

**任务**: 运行通道集成测试

**覆盖范围**:
- 多通道同时运行测试
- 消息路由测试

---

### 3. 端到端测试

#### 3.1 CLI 命令测试

| 测试命令 | 测试内容 | 状态 |
|----------|----------|------|
| `onboard` | 初始化配置和工作区 | ✅ 已覆盖 |
| `status` | 查看状态信息 | ✅ 已覆盖 |
| `config` | 配置管理 | ✅ 已覆盖 |

---

## 用户负责部分 (你)

以下测试需要你提供配置、外部服务访问权限或人工操作：

### 1. LLM Provider 集成测试

#### 1.1 需要配置: OpenAI

**需要你提供**:
- OpenAI API Key (设置环境变量 `OPENAI_API_KEY`)
- 或在 config.json 中配置

**测试内容**:
- 使用真实 LLM 进行对话
- 流式输出测试
- 函数调用测试

#### 1.2 需要配置: Ollama (本地)

**需要你提供**:
- Ollama 服务运行中 (默认 `http://localhost:11434`)
- 下载所需的模型 (如 llama2, codellama)

**测试内容**:
- 本地模型连接测试
- 模型列表获取

#### 1.3 可选: Anthropic / DeepSeek / Moonshot / Zhipu

**需要你提供**:
- 相应的 API Key
- API Base URL (如使用代理)

---

### 2. Channel 集成测试

#### 2.1 Telegram 通道

**需要你提供**:
1. Telegram Bot Token (从 @BotFather 获取)
2. 在 config.json 中配置:
   ```json
   {
     "channels": {
       "telegram": {
         "enabled": true,
         "token": "YOUR_BOT_TOKEN"
       }
     }
   }
   ```

**测试内容**:
- Bot 连接 Telegram
- 接收和发送消息
- Markdown 转换

**验证方法**: 
- 在 Telegram 中与 Bot 对话
- 发送 `/help` 命令验证响应

#### 2.2 Discord 通道

**需要你提供**:
1. Discord Bot Token
2. 将 Bot 添加到服务器
3. 配置:
   ```json
   {
     "channels": {
       "discord": {
         "enabled": true,
         "token": "YOUR_DISCORD_TOKEN"
       }
     }
   }
   ```

#### 2.3 Slack 通道

**需要你提供**:
1. Slack Bot Token (`xoxb-...`)
2. Slack App Token (`xapp-...`) (如果使用 Socket 模式)
3. 配置:
   ```json
   {
     "channels": {
       "slack": {
         "enabled": true,
         "botToken": "xoxb-...",
         "appToken": "xapp-..."
       }
     }
   }
   ```

#### 2.4 Email 通道

**需要你提供**:
1. Gmail 账户 (或支持 IMAP/SMTP 的邮箱)
2. 应用专用密码 (如使用 Gmail)
3. 配置:
   ```json
   {
     "channels": {
       "email": {
         "enabled": true,
         "smtpHost": "smtp.gmail.com",
         "smtpPort": 587,
         "smtpUser": "your@gmail.com",
         "smtpPassword": "app-password",
         "imapHost": "imap.gmail.com",
         "imapUser": "your@gmail.com",
         "imapPassword": "app-password"
       }
     }
   }
   ```

**注意**: Email 通道需要用户同意才能自动回复 (consent granted)

#### 2.5 其他通道 (可选测试)

| 通道 | 需要配置 | 难度 |
|------|----------|------|
| WhatsApp | Bridge URL + Token | 中 |
| Feishu (飞书) | App ID + App Secret | 中 |
| DingTalk (钉钉) | Client ID + Client Secret | 中 |
| QQ | App ID + Secret | 高 |
| Mochat | 自行部署服务 | 高 |

---

### 3. MCP (Model Context Protocol) 测试

#### 3.1 需要配置: MCP Server

**需要你提供**:
1. MCP Server 二进制文件或安装
2. 配置文件:
   ```json
   {
     "mcp": {
       "servers": {
         "filesystem": {
           "command": "mcp-server-filesystem",
           "args": ["--root", "/path/to/workspace"],
           "enabled": true
         }
       }
     }
   }
   ```

**测试内容**:
- MCP Server 连接
- 工具发现
- 远程工具调用

---

### 4. 心跳服务测试

**需要你提供**:
1. 至少一个启用的通道 (Telegram/Discord/Slack 等)
2. 配置:
   ```json
   {
     "heartbeat": {
       "enabled": true,
       "intervalSeconds": 300,
       "message": "检查一下，需要帮忙吗？"
     }
   }
   ```

**测试方法**:
- 等待或手动触发心跳
- 验证消息发送

---

### 5. 定时任务 (Cron) 测试

**需要你提供**:
1. 启用的通道
2. Agent 运行时使用 Cron 工具添加任务

**测试方法**:
- 使用 `/cron add` 工具添加定时任务
- 等待任务触发
- 验证消息发送

---

### 6. Skills 测试

#### 6.1 内置 Skills

| Skill | 功能 | 测试方法 |
|-------|------|----------|
| cron | 定时任务 | 使用 cron 工具 |
| github | GitHub 集成 | 配置 GitHub Token |
| memory | 记忆管理 | 与 Agent 对话 |
| skill-creator | 创建 Skill | 使用 /skill 命令 |
| summarize | 总结功能 | 分析长文本 |
| tmux | Tmux 集成 | 需要 tmux 环境 |
| weather | 天气查询 | 需要 API (可选) |

#### 6.2 自定义 Skills

**需要你提供**:
- 在 workspace/skills 目录中创建自定义 Skill
- 或使用 skill-creator Skill 创建

---

### 7. Docker 部署测试

**需要你提供**:
1. Docker 安装
2. 构建镜像:
   ```bash
   docker build -t nanobot-net .
   ```

**测试方法**:
```bash
# 运行 onboard
docker run --rm -it nanobot-net onboard --non-interactive

# 运行 status
docker run --rm -it nanobot-net status
```

---

## 测试执行时间表

### 阶段 1: 立即执行 (我负责)

| 步骤 | 操作 | 预计时间 |
|------|------|----------|
| 1.1 | 运行所有单元测试 | 1 分钟 |
| 1.2 | 修复失败测试 | 5 分钟 |
| 1.3 | 重新运行测试确认 | 1 分钟 |

### 阶段 2: 你配置 (你负责)

| 步骤 | 操作 | 预计时间 |
|------|------|----------|
| 2.1 | 配置 LLM Provider (OpenAI/Ollama) | 10 分钟 |
| 2.2 | 配置至少一个通道 (推荐 Telegram) | 15 分钟 |
| 2.3 | 验证配置生效 | 5 分钟 |

### 阶段 3: 集成测试 (我负责 + 你配合)

| 步骤 | 操作 | 负责方 |
|------|------|--------|
| 3.1 | 运行 Agent 集成测试 | 我 |
| 3.2 | 与 Bot 对话测试 | 你 |
| 3.3 | 工具调用测试 | 你 |
| 3.4 | 会话持久化测试 | 我 |

### 阶段 4: 可选高级测试 (你负责)

| 步骤 | 操作 | 预计时间 |
|------|------|----------|
| 4.1 | MCP Server 配置测试 | 30 分钟 |
| 4.2 | 心跳服务测试 | 10 分钟 |
| 4.3 | Cron 任务测试 | 15 分钟 |
| 4.4 | Docker 部署测试 | 20 分钟 |
| 4.5 | 多通道同时运行测试 | 20 分钟 |

---

## 快速开始

### 我需要做什么

1. ✅ 运行单元测试并修复失败
2. ✅ 运行集成测试
3. ✅ 验证 CLI 命令

### 你需要做什么

1. **设置 OpenAI API Key**:
   ```bash
   export OPENAI_API_KEY="your-key-here"
   ```

2. **配置 Telegram Bot** (推荐):
   - 从 @BotFather 创建 Bot
   - 获取 Token
   - 编辑 `~/.nbot/config.json`

3. **运行 Bot 并测试**:
   ```bash
   nbot agent
   ```

---

## 验证检查清单

### 开发者验证 (我)

- [ ] 所有单元测试通过
- [ ] 集成测试通过
- [ ] CLI 命令正常工作

### 用户验证 (你)

- [ ] LLM 连接成功
- [ ] 通道连接成功
- [ ] Bot 可以对话
- [ ] 工具可以调用
- [ ] 记忆可以保存

---

## 联系方式

如果测试过程中遇到问题:
1. 查看日志: `nbot agent` 输出
2. 检查配置: `nbot config --list`
3. 查看状态: `nbot status`

---

*文档版本: 2026-02-26*
