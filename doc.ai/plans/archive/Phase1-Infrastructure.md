# Phase 1: 基础设施层实现计划

本阶段实现 NanoBot.Net 的基础设施层，为上层模块提供配置管理、Workspace 管理和消息路由等核心能力。

## 阶段目标

建立稳定可靠的基础设施层，确保配置加载、文件系统操作和消息路由的正确性，为后续阶段提供坚实基础。

## 项目目录结构

本阶段涉及的项目目录结构如下：

```
src/
├── NanoBot.Core/                     # 核心抽象层
│   ├── Configuration/                # 配置模型（本阶段重点）
│   │   ├── Models/
│   │   │   ├── AgentConfig.cs        # 根配置
│   │   │   ├── WorkspaceConfig.cs    # Workspace 配置
│   │   │   ├── LlmConfig.cs          # LLM 配置
│   │   │   ├── ChannelsConfig.cs     # 通道配置集合
│   │   │   ├── SecurityConfig.cs     # 安全配置
│   │   │   ├── MemoryConfig.cs       # 记忆配置
│   │   │   ├── HeartbeatConfig.cs    # 心跳配置
│   │   │   └── McpConfig.cs          # MCP 配置
│   │   ├── Validators/
│   │   │   └── ConfigurationValidator.cs
│   │   └── Extensions/
│   │       └── ConfigurationExtensions.cs
│   ├── Workspace/                    # Workspace 抽象
│   │   ├── IWorkspaceManager.cs
│   │   └── IBootstrapLoader.cs
│   └── Bus/                          # 消息总线抽象
│       ├── IMessageBus.cs
│       ├── InboundMessage.cs
│       ├── OutboundMessage.cs
│       └── BusMessage.cs
│
├── NanoBot.Infrastructure/           # 基础设施实现
│   ├── Workspace/                    # Workspace 实现
│   │   ├── WorkspaceManager.cs
│   │   └── BootstrapLoader.cs
│   ├── Bus/                          # 消息总线实现
│   │   └── MessageBus.cs
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs
│
tests/
├── NanoBot.Core.Tests/               # 核心层测试
│   ├── Configuration/
│   │   └── ConfigurationTests.cs
│   ├── Workspace/
│   │   ├── WorkspaceManagerTests.cs
│   │   └── BootstrapLoaderTests.cs
│   └── Bus/
│       └── MessageBusTests.cs
│
└── NanoBot.Infrastructure.Tests/     # 基础设施测试
    ├── Workspace/
    │   └── WorkspaceTests.cs
    └── Bus/
        └── BusTests.cs
```

### 命名空间规范

| 模块 | 命名空间 | 说明 |
|------|----------|------|
| 配置模型 | `NanoBot.Core.Configuration` | 配置类定义 |
| Workspace 抽象 | `NanoBot.Core.Workspace` | 接口定义 |
| 消息总线抽象 | `NanoBot.Core.Bus` | 接口和消息类型 |
| Workspace 实现 | `NanoBot.Infrastructure.Workspace` | 实现类 |
| 消息总线实现 | `NanoBot.Infrastructure.Bus` | 实现类 |

### 文件命名规则

| 类型 | 命名规则 | 示例 |
|------|----------|------|
| 接口 | I + 名称 | `IWorkspaceManager.cs` |
| 实现类 | 功能名称 | `WorkspaceManager.cs` |
| 配置类 | 功能 + Config | `AgentConfig.cs` |
| 测试类 | 被测类 + Tests | `WorkspaceManagerTests.cs` |

## 相关方案文档

- [Configuration.md](../solutions/Configuration.md) - 配置管理层设计
- [Infrastructure.md](../solutions/Infrastructure.md) - 基础设施层设计

## 阶段依赖

本阶段为第一阶段，无前置依赖。

## 任务清单概览

| 任务清单 | 主要内容 | 并行度 | 状态 |
|----------|----------|--------|------|
| [配置管理模块](#任务清单-配置管理模块) | AgentConfig 及各类配置类实现 | 高 | ✅ 已完成 |
| [Workspace 管理模块](#任务清单-workspace-管理模块) | 目录结构管理与 Bootstrap 加载 | 高 | 待开始 |
| [消息总线模块](#任务清单-消息总线模块) | 消息队列与路由分发 | 高 | 待开始 |

## 任务清单：配置管理模块 ✅

> **状态**: 已完成 (2026-02-17)
> **测试结果**: 37 个测试全部通过

### 任务目标

实现完整的配置管理系统，支持 JSON 配置文件加载、环境变量替换、配置验证等功能。

### 相关方案文档

- [Configuration.md](../solutions/Configuration.md)

### 任务依赖

无前置依赖。

### 任务列表

#### Task 1.1.1: 创建配置项目结构 ✅

> **状态**: 已完成

**描述**: 创建 NanoBot.Core 项目中的 Configuration 模块，定义配置类所在的命名空间和目录结构。

**交付物**:
- src/NanoBot.Core/NanoBot.Core.csproj 项目文件
- src/NanoBot.Core/Configuration/ 目录
- src/NanoBot.Core/Configuration/Models/ 目录
- src/NanoBot.Core/Configuration/Validators/ 目录
- src/NanoBot.Core/Configuration/Extensions/ 目录

**完成标准**:
- 项目可成功编译
- 目录结构符合 .NET 规范
- 命名空间为 `NanoBot.Core.Configuration`

---

#### Task 1.1.2: 实现 AgentConfig 根配置类 ✅

> **状态**: 已完成

**描述**: 实现 AgentConfig 根配置类，包含所有子配置的聚合。

**交付物**:
- src/NanoBot.Core/Configuration/Models/AgentConfig.cs 文件
- 包含 Name、Workspace、Llm、Channels、Mcp、Security、Memory、Heartbeat 属性

**完成标准**:
- 类定义完整，属性类型正确
- 支持默认值初始化
- 命名空间为 `NanoBot.Core.Configuration`

---

#### Task 1.1.3: 实现 WorkspaceConfig 配置类 ✅

> **状态**: 已完成

**描述**: 实现 WorkspaceConfig 类，提供路径解析和目录访问方法。

**交付物**:
- src/NanoBot.Core/Configuration/Models/WorkspaceConfig.cs 文件
- GetResolvedPath() 方法实现
- 各子目录路径获取方法

**完成标准**:
- 支持 ~ 路径展开
- 所有路径方法返回正确的绝对路径
- 命名空间为 `NanoBot.Core.Configuration`

---

#### Task 1.1.4: 实现 LlmConfig 配置类 ✅

> **状态**: 已完成

**描述**: 实现 LLM 模型配置类。

**交付物**:
- src/NanoBot.Core/Configuration/Models/LlmConfig.cs 文件
- 包含 Model、ApiKey、ApiBase、Provider、Temperature、MaxTokens 属性

**完成标准**:
- 属性定义完整
- 默认值符合设计规范
- 命名空间为 `NanoBot.Core.Configuration`

---

#### Task 1.1.5: 实现 SecurityConfig 配置类 ✅

> **状态**: 已完成

**描述**: 实现安全配置类，控制文件和命令访问权限。

**交付物**:
- src/NanoBot.Core/Configuration/Models/SecurityConfig.cs 文件
- AllowedDirs、DenyCommandPatterns、RestrictToWorkspace、ShellTimeout 属性

**完成标准**:
- 安全限制配置完整
- 默认值确保基本安全
- 命名空间为 `NanoBot.Core.Configuration`

---

#### Task 1.1.6: 实现 MemoryConfig 配置类 ✅

> **状态**: 已完成

**描述**: 实现记忆系统配置类。

**交付物**:
- src/NanoBot.Core/Configuration/Models/MemoryConfig.cs 文件
- MemoryFile、HistoryFile、MaxHistoryEntries、Enabled 属性

**完成标准**:
- 配置项完整
- 默认值合理
- 命名空间为 `NanoBot.Core.Configuration`

---

#### Task 1.1.7: 实现 HeartbeatConfig 配置类 ✅

> **状态**: 已完成

**描述**: 实现心跳服务配置类。

**交付物**:
- src/NanoBot.Core/Configuration/Models/HeartbeatConfig.cs 文件
- Enabled、IntervalSeconds、Message 属性

**完成标准**:
- 配置项完整
- 命名空间为 `NanoBot.Core.Configuration`

---

#### Task 1.1.8: 实现 McpConfig 配置类 ✅

> **状态**: 已完成

**描述**: 实现 MCP 服务器配置类。

**交付物**:
- src/NanoBot.Core/Configuration/Models/McpConfig.cs 文件
- src/NanoBot.Core/Configuration/Models/McpServerConfig.cs 文件
- Servers 字典属性

**完成标准**:
- 支持多服务器配置
- 包含 Command、Args、Env、Cwd 属性
- 命名空间为 `NanoBot.Core.Configuration`

---

#### Task 1.1.9: 实现通道配置类 ✅

> **状态**: 已完成

**描述**: 实现所有通道配置类（Telegram、Discord、Feishu、WhatsApp、DingTalk、Email、Slack、QQ、Mochat）。

**交付物**:
- src/NanoBot.Core/Configuration/Models/ChannelsConfig.cs 文件
- src/NanoBot.Core/Configuration/Models/Channels/ 目录
- 各通道配置类文件（TelegramConfig.cs 等）

**完成标准**:
- 所有通道配置类定义完整
- 属性与设计文档一致
- 命名空间为 `NanoBot.Core.Configuration`

---

#### Task 1.1.10: 实现配置加载器 ✅

> **状态**: 已完成

**描述**: 实现配置加载逻辑，支持 JSON 文件和环境变量。

**交付物**:
- src/NanoBot.Core/Configuration/Extensions/ConfigurationLoader.cs 文件
- 环境变量替换逻辑

**完成标准**:
- 支持 ${VAR_NAME} 语法
- 配置热重载支持
- 命名空间为 `NanoBot.Core.Configuration`

---

#### Task 1.1.11: 实现配置验证器 ✅

> **状态**: 已完成

**描述**: 实现配置验证逻辑，确保配置正确性。

**交付物**:
- src/NanoBot.Core/Configuration/Validators/ConfigurationValidator.cs 文件
- 各配置类的验证规则

**完成标准**:
- 必填字段验证
- 格式验证（URL、路径）
- 逻辑验证
- 命名空间为 `NanoBot.Core.Configuration`

---

#### Task 1.1.12: 编写配置模块单元测试 ✅

> **状态**: 已完成
> **测试结果**: 37 个测试全部通过

**描述**: 编写配置模块的单元测试。

**交付物**:
- tests/NanoBot.Core.Tests/NanoBot.Core.Tests.csproj 项目文件
- tests/NanoBot.Core.Tests/Configuration/ 目录
- tests/NanoBot.Core.Tests/Configuration/ConfigurationTests.cs 文件

**完成标准**:
- 测试覆盖率 >= 80%
- 所有测试通过
- 命名空间为 `NanoBot.Core.Tests.Configuration`

### 成功指标

| 指标 | 状态 |
|------|------|
| 配置加载成功率 100% | ✅ 已达成 |
| 环境变量替换正确 | ✅ 已达成 |
| 配置验证覆盖所有必填字段 | ✅ 已达成 |
| 单元测试覆盖率 >= 80% | ✅ 已达成 (37 个测试全部通过) |

---

## 任务清单：Workspace 管理模块

### 任务目标

实现 Workspace 目录结构管理和 Bootstrap 文件加载功能。

### 相关方案文档

- [Infrastructure.md](../solutions/Infrastructure.md) - IWorkspaceManager、IBootstrapLoader 接口

### 任务依赖

- 配置管理模块（需要 WorkspaceConfig）

### 任务列表

#### Task 1.2.1: 定义 IWorkspaceManager 接口

**描述**: 定义 Workspace 管理器接口。

**交付物**:
- src/NanoBot.Core/Workspace/IWorkspaceManager.cs 接口文件
- 所有路径获取方法声明
- 文件操作方法声明

**完成标准**:
- 接口定义与设计文档一致
- 包含 InitializeAsync 方法
- 命名空间为 `NanoBot.Core.Workspace`

---

#### Task 1.2.2: 实现 WorkspaceManager 类

**描述**: 实现 Workspace 管理器，提供目录和文件操作。

**交付物**:
- src/NanoBot.Infrastructure/Workspace/WorkspaceManager.cs 实现文件
- 目录初始化逻辑
- 文件读写方法

**完成标准**:
- 目录结构正确创建
- 文件操作正确实现
- 路径解析正确
- 命名空间为 `NanoBot.Infrastructure.Workspace`

---

#### Task 1.2.3: 实现 Workspace 初始化逻辑

**描述**: 实现首次运行时的 Workspace 初始化，创建默认文件。

**交付物**:
- src/NanoBot.Infrastructure/Workspace/WorkspaceManager.cs 中 InitializeAsync 方法完整实现
- 默认文件内容模板

**完成标准**:
- 创建 memory/、skills/、sessions/ 目录
- 创建 AGENTS.md、SOUL.md、TOOLS.md、USER.md、HEARTBEAT.md 默认文件
- 创建空的 MEMORY.md、HISTORY.md 文件

---

#### Task 1.2.4: 定义 IBootstrapLoader 接口

**描述**: 定义 Bootstrap 文件加载器接口。

**交付物**:
- src/NanoBot.Core/Workspace/IBootstrapLoader.cs 接口文件
- LoadAllBootstrapFilesAsync 方法声明
- 各单独加载方法声明

**完成标准**:
- 接口定义与设计文档一致
- BootstrapFiles 属性定义
- 命名空间为 `NanoBot.Core.Workspace`

---

#### Task 1.2.5: 实现 BootstrapLoader 类

**描述**: 实现 Bootstrap 文件加载器。

**交付物**:
- src/NanoBot.Infrastructure/Workspace/BootstrapLoader.cs 实现文件
- 文件加载逻辑
- 内容拼接逻辑

**完成标准**:
- 正确加载所有 Bootstrap 文件
- 文件不存在时返回 null
- LoadAllBootstrapFilesAsync 返回拼接内容
- 命名空间为 `NanoBot.Infrastructure.Workspace`

---

#### Task 1.2.6: 编写 Workspace 模块单元测试

**描述**: 编写 Workspace 管理模块的单元测试。

**交付物**:
- tests/NanoBot.Infrastructure.Tests/NanoBot.Infrastructure.Tests.csproj 项目文件
- tests/NanoBot.Infrastructure.Tests/Workspace/ 目录
- tests/NanoBot.Infrastructure.Tests/Workspace/WorkspaceManagerTests.cs 文件
- tests/NanoBot.Infrastructure.Tests/Workspace/BootstrapLoaderTests.cs 文件

**完成标准**:
- 测试覆盖率 >= 80%
- 所有测试通过
- 包含边界条件测试
- 命名空间为 `NanoBot.Infrastructure.Tests.Workspace`

### 成功指标

- Workspace 初始化成功率 100%
- Bootstrap 文件加载正确
- 单元测试覆盖率 >= 80%

---

## 任务清单：消息总线模块 ✅ 已完成

### 任务目标

实现消息路由和队列管理，支持入站/出站消息的双向传递。

### 相关方案文档

- [Infrastructure.md](../solutions/Infrastructure.md) - IMessageBus 接口
- [Channels.md](../solutions/Channels.md) - InboundMessage、OutboundMessage 定义

### 任务依赖

无前置依赖。

### 任务列表

#### Task 1.3.1: 定义消息类型 ✅

**描述**: 定义消息总线使用的消息类型。

**交付物**:
- src/NanoBot.Core/Bus/InboundMessage.cs 文件
- src/NanoBot.Core/Bus/OutboundMessage.cs 文件
- src/NanoBot.Core/Bus/BusMessage.cs 文件
- src/NanoBot.Core/Bus/BusMessageType.cs 枚举

**完成标准**:
- 消息类型定义与设计文档一致
- SessionKey 属性计算正确
- 命名空间为 `NanoBot.Core.Bus`

---

#### Task 1.3.2: 定义 IMessageBus 接口 ✅

**描述**: 定义消息总线接口。

**交付物**:
- src/NanoBot.Core/Bus/IMessageBus.cs 接口文件
- 发布/消费方法声明
- 分发器方法声明

**完成标准**:
- 接口定义与设计文档一致
- 包含 InboundSize、OutboundSize 属性
- 命名空间为 `NanoBot.Core.Bus`

---

#### Task 1.3.3: 实现 MessageBus 类 ✅

**描述**: 基于 System.Threading.Channels 实现消息总线。

**交付物**:
- src/NanoBot.Infrastructure/Bus/MessageBus.cs 实现文件
- 入站/出站 Channel 实现
- 线程安全保证

**完成标准**:
- 支持多生产者/多消费者
- 正确处理取消令牌
- 实现 IDisposable
- 命名空间为 `NanoBot.Infrastructure.Bus`

---

#### Task 1.3.4: 实现消息分发器 ✅

**描述**: 实现出站消息的分发逻辑。

**交付物**:
- src/NanoBot.Infrastructure/Bus/MessageBus.cs 中 StartDispatcherAsync 方法实现
- src/NanoBot.Infrastructure/Bus/MessageBus.cs 中 SubscribeOutbound 方法实现
- 通道回调管理

**完成标准**:
- 正确路由消息到目标通道
- 支持多通道订阅
- 异常处理正确

---

#### Task 1.3.5: 编写消息总线单元测试 ✅

**描述**: 编写消息总线的单元测试。

**交付物**:
- tests/NanoBot.Infrastructure.Tests/Bus/ 目录
- tests/NanoBot.Infrastructure.Tests/Bus/MessageBusTests.cs 文件

**完成标准**:
- 测试发布/消费流程
- 测试分发器路由
- 测试取消和停止
- 测试覆盖率 >= 80%
- 命名空间为 `NanoBot.Infrastructure.Tests.Bus`

### 成功指标

- ✅ 消息传递无丢失
- ✅ 并发处理正确
- ✅ 单元测试覆盖率 >= 80%（57 个测试全部通过）

---

## 风险评估

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|----------|
| 配置格式变更 | 高 | 低 | 设计灵活的配置结构，支持向后兼容 |
| 文件权限问题 | 中 | 中 | 提供清晰的错误提示，支持权限检查 |
| 消息队列溢出 | 高 | 低 | 实现背压机制，限制队列大小 |

## 阶段完成标准

- 所有任务清单完成
- 所有单元测试通过
- 代码审查通过
- 文档更新完成

## 下一阶段

完成本阶段后，进入 [Phase 2: 核心服务层](./Phase2-Core-Services.md)。
