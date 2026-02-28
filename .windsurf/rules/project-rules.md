---
trigger: always_on
---
# 项目介绍

NanoBot.Net 是 nanobot 的 .NET 移植版 —— 一个超轻量级个人 AI 助手。核心设计遵循 nanobot（约 4,000 行哲学），用 C# 重写并做必要的 .NET 优化与扩展。作为 .NET 生态中 AI Agent 产品的核心组件。

# 目标

1. 完整移植 nanobot 的所有能力到 .NET 平台
2. 保持代码精简
3. 基于 Microsoft.Agents.AI 框架实现

# 技术栈

- **语言/运行时**: C# / .NET 8+ (LTS)
- **核心框架**: Microsoft.Agents.AI
- **依赖注入**: Microsoft.Extensions.DependencyInjection
- **配置管理**: Microsoft.Extensions.Configuration
- **日志系统**: Microsoft.Extensions.Logging
- **JSON 处理**: System.Text.Json
- **CLI 框架**: System.CommandLine

# 项目结构

工作目录带有nanobot的源码和Microsoft.Agents.AI源码，.net移植版必须基于Microsoft.Agents.AI实现，禁止重复造轮子。

```
 src/
├── NanoBot.Core/           # 核心抽象层（接口、模型、配置）
├── NanoBot.Infrastructure/ # 基础设施实现（Bus、Workspace、资源加载）
├── NanoBot.Agent/          # Agent 核心实现（循环、上下文、记忆）
├── NanoBot.Providers/      # LLM 提供商实现
├── NanoBot.Tools/          # 工具实现
├── NanoBot.Channels/       # 通道实现
├── NanoBot.Cli/            # 命令行入口
├── workspace/              # 嵌入式资源（AGENTS.md、SOUL.md 等）
└── skills/                 # 内置 Skills（嵌入式）

tests/                      # 测试项目（与 src 镜像结构）
doc.ai/                     # 架构设计文档

Temp/
├── agent-framework/              # Microsoft.Agents.AI的源码，包括文档。
└── nanobot/                 # nanobot的原项目代码。
注意：Temp目录在.gitignore中。
```

# 命名规范

- **命名空间**: `NanoBot.{模块}.{子模块}`，如 `NanoBot.Core.Agents`
- **文件夹**: PascalCase，单数形式
- **接口**: `I` 前缀，如 `IAgent`
- **测试类**: `{被测类}Tests`，如 `AgentTests`

# 核心原则

1. **精简优先**: 保持代码简洁，避免过度设计
2. **接口隔离**: 核心层只定义抽象，实现放在独立项目
3. **依赖注入**: 所有服务通过 DI 容器管理
4. **配置驱动**: 支持 JSON 配置和环境变量
5. **测试覆盖**: 核心逻辑必须有单元测试

# 开发约定

- 使用 `async/await` 进行异步编程
- 优先使用 `System.Text.Json` 进行序列化
- 使用 `IHttpClientFactory` 管理 HTTP 客户端
- 错误处理使用异常而非返回码
- 日志使用 `ILogger<T>` 泛型接口

# 项目管理

- 当执行doc.ai/plans中的计划时，每个任务完成后都需要更新计划文档中的任务状态；
- 执行计划时，计划相关的设计文档在 `doc.ai/solutions`中，必须阅读；
- 执行计划时，计划相关的原项目的源代码必须阅读；
- 在编写doc.ai/solutions中的方案设计文档时，禁止把实现代码写到方案中，但允许编写接口类、属性、方法等基本的签名定义。
- 在提交代码时，不允许提供`Temp`目录。
