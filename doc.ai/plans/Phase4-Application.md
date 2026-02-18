# Phase 4: 应用层实现计划（基于 Microsoft.Agents.AI）

本阶段实现 NanoBot.Net 的应用层，包括 CLI 命令、依赖注入配置、测试和部署。

## 阶段目标

实现完整的应用入口，支持命令行交互和 Gateway 服务模式，完成测试和部署准备。

## 核心原则

### DI 注册策略

使用 Microsoft.Agents.AI 框架后，DI 注册需要调整：

| 原注册方式 | 新注册方式 |
|-----------|-----------|
| `AddSingleton<ILLMProvider, OpenAIProvider>()` | `AddSingleton<IChatClient>(sp => CreateChatClient())` |
| `AddSingleton<IToolRegistry, ToolRegistry>()` | `AddSingleton<IReadOnlyList<AITool>>(sp => CreateTools())` |
| `AddSingleton<IAgent, Agent>()` | `AddSingleton<NanoBotAgent>()` |

---

## 相关方案文档

- [CLI.md](../solutions/CLI.md) - CLI 命令层设计
- [Testing.md](../solutions/Testing.md) - 测试方案设计
- [Installation.md](../solutions/Installation.md) - 安装程序设计

## 阶段依赖

- Phase 1-2 重构已完成
- Phase 3 Agent 核心层已完成
- Microsoft.Agents.AI 包已引用
- 所有核心模块可用

---

## 任务清单概览

| 任务清单 | 主要内容 | 并行度 |
|----------|----------|--------|
| [依赖注入配置模块](#任务清单-依赖注入配置模块) | DI 容器配置 | 高 |
| [CLI 命令模块](#任务清单-cli-命令模块) | 各 CLI 命令实现 | 高 |
| [集成测试模块](#任务清单-集成测试模块) | 端到端测试 | 中 |
| [部署准备模块](#任务清单-部署准备模块) | Docker 和发布配置 | 高 |
| [安装程序模块](#任务清单-安装程序模块) | 安装脚本和包管理器 | 高 |

---

## 任务清单：依赖注入配置模块

### 任务目标

实现完整的依赖注入配置，正确注册 Microsoft.Agents.AI 和 NanoBot 特有服务。

### 任务依赖

- 所有核心模块已完成

### 任务列表

#### Task 4.1.1: 创建服务扩展方法基类

**描述**: 创建服务注册的扩展方法基础设施。

**交付物**:
- `NanoBot.Cli/Extensions/ServiceCollectionExtensions.cs` 文件
- `AddNanoBot` 扩展方法

**完成标准**:
- 支持链式调用
- 支持配置选项

---

#### Task 4.1.2: 实现框架服务注册

**描述**: 注册 Microsoft.Agents.AI 框架服务。

**交付物**:
- `AddMicrosoftAgentsAI` 扩展方法
- 注册 `IChatClient` 工厂
- 注册 `ChatClientAgent` 相关服务

**完成标准**:
- 框架服务正确注册
- `IChatClient` 可正确创建

**示例代码**:
```csharp
public static IServiceCollection AddMicrosoftAgentsAI(
    this IServiceCollection services,
    LlmConfig config)
{
    services.AddSingleton<IChatClient>(sp =>
    {
        return config.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAIClient(config.ApiKey).GetChatClient(config.Model),
            "azure" => new AzureOpenAIClient(new Uri(config.Endpoint!), new AzureCliCredential()).GetChatClient(config.Model),
            _ => CreateOpenAICompatibleClient(config)
        };
    });
    
    return services;
}
```

---

#### Task 4.1.3: 实现工具服务注册

**描述**: 注册工具服务（返回 `AITool` 列表）。

**交付物**:
- `AddNanoBotTools` 扩展方法
- 注册 `IReadOnlyList<AITool>`

**完成标准**:
- 所有内置工具正确注册
- 支持 MCP 工具合并

**示例代码**:
```csharp
public static IServiceCollection AddNanoBotTools(
    this IServiceCollection services,
    string workspacePath)
{
    services.AddSingleton<IReadOnlyList<AITool>>(sp =>
    {
        var tools = new List<AITool>
        {
            FileTools.CreateReadFileTool(workspacePath),
            FileTools.CreateWriteFileTool(workspacePath),
            FileTools.CreateEditFileTool(workspacePath),
            FileTools.CreateListDirTool(workspacePath),
            ShellTools.CreateExecTool(workspacePath),
            WebTools.CreateWebSearchTool(),
            WebTools.CreateWebFetchTool()
        };
        
        var messageBus = sp.GetService<IMessageBus>();
        if (messageBus != null)
        {
            tools.Add(MessageTools.CreateMessageTool(messageBus));
        }
        
        return tools.AsReadOnly();
    });
    
    return services;
}
```

---

#### Task 4.1.4: 实现上下文提供者注册

**描述**: 注册 AIContextProvider 实现。

**交付物**:
- `AddNanoBotContextProviders` 扩展方法
- 注册所有 ContextProvider

**完成标准**:
- 所有 ContextProvider 正确注册
- 框架可正确调用

---

#### Task 4.1.5: 实现基础设施服务注册

**描述**: 注册 nanobot 特有的基础设施服务。

**交付物**:
- `AddNanoBotInfrastructure` 扩展方法
- 注册 `IMessageBus`、`IWorkspaceManager` 等

**完成标准**:
- 基础设施服务正确注册

---

#### Task 4.1.6: 实现后台服务注册

**描述**: 注册后台服务。

**交付物**:
- `AddNanoBotServices` 扩展方法
- 注册 `ICronService`、`IHeartbeatService`、`ISkillsLoader`

**完成标准**:
- 后台服务正确注册
- 支持托管服务

---

#### Task 4.1.7: 实现通道服务注册

**描述**: 注册通道服务。

**交付物**:
- `AddNanoBotChannels` 扩展方法
- 注册 `IChannelManager` 和各通道

**完成标准**:
- 通道正确注册
- 根据配置启用

---

#### Task 4.1.8: 实现完整服务注册

**描述**: 实现聚合所有服务的扩展方法。

**交付物**:
- `AddNanoBot` 扩展方法
- 调用所有子模块注册

**完成标准**:
- 一键注册所有服务
- 支持选择性注册

**示例代码**:
```csharp
public static IServiceCollection AddNanoBot(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var config = configuration.GetSection("Agent").Get<AgentConfig>() ?? new AgentConfig();
    
    services
        .AddNanoBotConfiguration(configuration)
        .AddMicrosoftAgentsAI(config.Llm)
        .AddNanoBotInfrastructure(config.Workspace.GetResolvedPath())
        .AddNanoBotTools(config.Workspace.GetResolvedPath())
        .AddNanoBotContextProviders()
        .AddNanoBotServices()
        .AddNanoBotChannels(config.Channels)
        .AddNanoBotAgent();
    
    return services;
}
```

---

#### Task 4.1.9: 编写 DI 配置单元测试

**描述**: 编写依赖注入配置的单元测试。

**交付物**:
- `NanoBot.Cli.Tests/ServiceCollectionTests.cs` 文件

**完成标准**:
- 所有服务可正确解析
- 生命周期正确

### 成功指标

- 所有服务正确注册
- 依赖链完整
- 服务解析无异常

---

## 任务清单：CLI 命令模块

### 任务目标

实现所有 CLI 命令，支持命令行交互和 Gateway 服务模式。

### 相关方案文档

- [CLI.md](../solutions/CLI.md)

### 任务依赖

- 依赖注入配置模块
- 所有核心模块

### 任务列表

#### Task 4.2.1: 定义 CLI 命令结构

**描述**: 定义 CLI 命令的基础结构。

**交付物**:
- `NanoBot.Cli/Commands/` 目录
- 命令基类或接口

**完成标准**:
- 命令结构清晰
- 支持 System.CommandLine

---

#### Task 4.2.2: 实现 onboard 命令

**描述**: 实现初始化 Agent 工作目录命令。

**交付物**:
- `OnboardCommand.cs` 文件
- 目录创建逻辑
- 默认文件生成

**完成标准**:
- 正确创建目录结构
- 正确生成默认文件

---

#### Task 4.2.3: 实现 agent 命令

**描述**: 实现启动 Agent 交互模式命令。

**交付物**:
- `AgentCommand.cs` 文件
- REPL 循环实现
- 调用 `NanoBotAgent`

**完成标准**:
- 正确启动 Agent
- 支持多轮对话
- 支持退出命令

---

#### Task 4.2.4: 实现 gateway 命令

**描述**: 实现启动 Gateway 服务模式命令。

**交付物**:
- `GatewayCommand.cs` 文件
- 多通道启动逻辑
- HTTP 健康检查端点

**完成标准**:
- 正确启动所有通道
- 支持健康检查
- 支持优雅关闭

---

#### Task 4.2.5: 实现 status 命令

**描述**: 实现显示 Agent 状态命令。

**交付物**:
- `StatusCommand.cs` 文件
- 状态收集逻辑

**完成标准**:
- 正确显示 Agent 信息
- 正确显示通道状态

---

#### Task 4.2.6: 实现 config 命令

**描述**: 实现配置管理命令。

**交付物**:
- `ConfigCommand.cs` 文件
- 配置读写逻辑

**完成标准**:
- 支持列出配置
- 支持获取/设置配置项

---

#### Task 4.2.7: 实现 session 命令

**描述**: 实现会话管理命令。

**交付物**:
- `SessionCommand.cs` 文件
- 会话操作逻辑

**完成标准**:
- 支持列出会话
- 支持清除会话

---

#### Task 4.2.8: 实现 cron 命令

**描述**: 实现定时任务管理命令。

**交付物**:
- `CronCommand.cs` 文件
- 任务操作逻辑

**完成标准**:
- 支持列出/添加/删除任务

---

#### Task 4.2.9: 实现 mcp 命令

**描述**: 实现 MCP 服务器管理命令。

**交付物**:
- `McpCommand.cs` 文件
- MCP 操作逻辑

**完成标准**:
- 支持列出服务器和工具

---

#### Task 4.2.10: 实现程序入口

**描述**: 实现主程序入口和命令行解析。

**交付物**:
- `Program.cs` 文件
- System.CommandLine 集成

**完成标准**:
- 正确解析命令行参数
- 支持帮助信息

---

#### Task 4.2.11: 编写 CLI 命令单元测试

**描述**: 编写 CLI 命令的单元测试。

**交付物**:
- `NanoBot.Cli.Tests/` 目录
- 各命令测试文件

**完成标准**:
- 测试覆盖率 >= 75%
- 所有测试通过

### 成功指标

- 所有命令正确实现
- 命令行解析正确
- 单元测试覆盖率 >= 75%

---

## 任务清单：集成测试模块

### 任务目标

实现端到端测试和集成测试，验证系统整体功能。

### 任务依赖

- 所有模块已完成

### 任务列表

#### Task 4.3.1: 创建集成测试项目

**描述**: 创建集成测试项目结构。

**交付物**:
- `NanoBot.Integration.Tests` 项目

**完成标准**:
- 项目结构正确

---

#### Task 4.3.2: 实现 Agent 集成测试

**描述**: 实现 Agent 的端到端测试。

**交付物**:
- `AgentIntegrationTests.cs` 文件
- 完整流程测试

**完成标准**:
- 测试消息处理流程
- 测试工具调用流程

---

#### Task 4.3.3: 实现通道集成测试

**描述**: 实现通道的集成测试。

**交付物**:
- `ChannelIntegrationTests.cs` 文件

**完成标准**:
- 测试消息收发

---

#### Task 4.3.4: 实现测试工具和 Mock

**描述**: 实现测试辅助工具。

**交付物**:
- `MockChatClient.cs` 文件
- `TestFixture.cs` 文件

**完成标准**:
- Mock 对象正确模拟行为

---

#### Task 4.3.5: 编写 Docker 测试脚本

**描述**: 编写 Docker 环境的测试脚本。

**交付物**:
- `test-docker.sh` 脚本

**完成标准**:
- 正确运行测试

### 成功指标

- 集成测试覆盖主要流程
- 端到端测试通过

---

## 任务清单：部署准备模块

### 任务目标

准备 Docker 部署和发布配置。

### 任务依赖

- 所有模块已完成
- 集成测试通过

### 任务列表

#### Task 4.4.1: 创建 Dockerfile

**描述**: 创建 Docker 镜像构建文件。

**交付物**:
- `Dockerfile` 文件

**完成标准**:
- 镜像大小合理
- 构建过程正确

---

#### Task 4.4.2: 创建 docker-compose 配置

**描述**: 创建 Docker Compose 编排配置。

**交付物**:
- `docker-compose.yml` 文件

**完成标准**:
- 正确编排服务

---

#### Task 4.4.3: 创建发布配置

**描述**: 创建 .NET 发布配置。

**交付物**:
- 发布脚本
- 发布配置文件

**完成标准**:
- 支持多平台发布

---

#### Task 4.4.4: 创建示例配置文件

**描述**: 创建示例配置文件。

**交付物**:
- `config.example.json` 文件

**完成标准**:
- 配置示例完整

---

#### Task 4.4.5: 验证部署流程

**描述**: 验证完整的部署流程。

**交付物**:
- 部署验证报告

**完成标准**:
- Docker 构建成功
- 功能验证通过

### 成功指标

- Docker 镜像构建成功
- 发布包可用

---

## 任务清单：安装程序模块

### 任务目标

实现完整的安装程序和分发方案。

### 任务依赖

- 部署准备模块

### 任务列表

#### Task 4.5.1: 创建 Unix 安装脚本

**描述**: 创建 macOS/Linux 安装脚本。

**交付物**:
- `scripts/install.sh` 文件

**完成标准**:
- 支持多平台

---

#### Task 4.5.2: 创建 Windows 安装脚本

**描述**: 创建 Windows PowerShell 安装脚本。

**交付物**:
- `scripts/install.ps1` 文件

**完成标准**:
- 支持 Windows

---

#### Task 4.5.3: 创建发布构建脚本

**描述**: 创建多平台发布构建脚本。

**交付物**:
- `scripts/publish.sh` 文件

**完成标准**:
- 支持 6 个目标平台

---

#### Task 4.5.4: 创建 Homebrew Tap

**描述**: 创建 Homebrew Tap 仓库。

**交付物**:
- `homebrew-nanobot/` 目录
- `Formula/nanobot.rb` 文件

**完成标准**:
- brew install 可用

---

#### Task 4.5.5: 配置 dotnet tool 打包

**描述**: 配置 NuGet 包发布为 dotnet tool。

**交付物**:
- `NanoBot.Cli.csproj` 更新

**完成标准**:
- dotnet tool install 可用

---

#### Task 4.5.6: 创建 GitHub Actions 发布工作流

**描述**: 创建自动化发布工作流。

**交付物**:
- `.github/workflows/release.yml` 文件

**完成标准**:
- 标签触发自动构建

---

#### Task 4.5.7: 验证安装流程

**描述**: 验证所有安装方式。

**交付物**:
- 验证报告

**完成标准**:
- 所有安装方式可用

---

#### Task 4.5.8: 更新 README 安装说明

**描述**: 更新项目 README。

**交付物**:
- `README.md` 更新

**完成标准**:
- 安装说明完整

### 成功指标

- 所有安装脚本可用
- GitHub Actions 发布流程正确

---

## 风险评估

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|----------|
| CLI 命令解析错误 | 中 | 低 | 完善单元测试 |
| 服务注册遗漏 | 高 | 低 | 自动化注册验证 |
| 部署环境差异 | 中 | 中 | 多环境测试 |

---

## 阶段完成标准

- [ ] 所有服务正确注册
- [ ] 所有 CLI 命令实现完成
- [ ] 所有测试通过
- [ ] Docker 部署验证通过
- [ ] 安装程序可用

## 项目完成

完成本阶段后，NanoBot.Net 项目实现完成，可进入维护和迭代阶段。
