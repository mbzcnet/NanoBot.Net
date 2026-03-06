# NanoBot.Net 架构优化方案

**日期**: 2026-03-03
**目标**: 解决 WebUI 代码审核中发现的架构问题
**原则**: 精简优先，避免过度设计

---

## 1. 会话文件路径问题分析

### 问题描述

当前存在**两个地方**都在处理会话文件：

| 位置                       | 存储格式               | 用途                            |
| -------------------------- | ---------------------- | ------------------------------- |
| `SessionManager` (Agent) | `{sessionKey}.jsonl` | Agent 会话历史                  |
| `SessionService` (WebUI) | `sessions_meta.json` | WebUI 元数据（标题、ProfileId） |

### 解决方案 ✅

**删除 `sessions_meta.json`**，统一使用 Agent 的 `SessionManager` 管理会话，元数据存储在现有 `.jsonl` 文件的 metadata 行中。

具体做法：

1. 扩展 `.jsonl` metadata 行，添加 `title` 和 `profile_id` 字段
2. 修改 `SessionManager` 支持这些字段的读写
3. 删除 `SessionService` 中的 `sessions_meta.json` 读写逻辑

---

## 2. 统一业务模型 ✅

### 当前问题

业务模型分散在多个项目中，存在命名冲突和结构不一致：

| 模型 | 当前位置 | 问题 |
|------|----------|------|
| `SessionInfo` | Agent + WebUI | 两处定义，结构不同，命名冲突 |
| `MessageInfo` | WebUI | 位置不当，应在 Core 层 |
| `AttachmentInfo` | WebUI | 位置不当，应在 Core 层 |
| `ISessionService` | WebUI | 位置不当，应在 Core 层 |
| `IAgentService` | WebUI | 位置不当，应在 Core 层 |

### 解决方案

在 `NanoBot.Core/Sessions/` 中统一定义所有业务模型：

```
NanoBot.Core/Sessions/
├── ISessionService.cs      # 会话服务接口
├── SessionInfo.cs          # 会话信息模型
├── MessageInfo.cs          # 消息信息模型
└── AttachmentInfo.cs       # 附件信息模型
```

Agent 层的 `SessionInfo` 重命名为 `SessionFileInfo`（内部使用）。

---

## 3. 简化服务注册方案 ✅

### 当前问题

WebUI 项目手动调用各个服务注册方法，与 CLI 项目重复。

### 解决方案

**WebUI 直接使用 CLI 的 `AddNanoBot()` 方法**，无需创建新项目。

```
┌─────────────────────────────────────────────────────────────────┐
│                         应用入口层                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐             │
│  │  NanoBot.Cli │  │NanoBot.WebUI│  │NanoBot.Channels│         │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘             │
└─────────┼────────────────┼────────────────┼─────────────────────┘
          │                │                │
          │    ┌───────────┘                │
          │    │ 直接使用 CLI 的方法         │
          ▼    ▼                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    NanoBot.Cli.Extensions                       │
│  - AddNanoBot() 统一服务注册                                     │
└─────────────────────────────────────────────────────────────────┘
         │                     │                     │
         └─────────────────────┼─────────────────────┘
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                    NanoBot.Core (接口定义)                        │
│  - ISessionService, IAgentService                               │
│  - SessionInfo, MessageInfo, AttachmentInfo                     │
└─────────────────────────────────────────────────────────────────┘
```

**核心原则**：CLI 项目作为服务注册的基础设施，WebUI 和 Channels 直接复用。

---

## 4. SessionManager 扩展 ✅

### 扩展 metadata 结构

在 metadata 行中存储 `title` 和 `profile_id`：

```csharp
// SessionManager.cs - 扩展 metadata 结构
var metadataLine = new JsonObject
{
    ["_type"] = "metadata",
    ["key"] = sessionKey,
    ["created_at"] = createdAt.ToString("o"),
    ["updated_at"] = DateTimeOffset.Now.ToString("o"),
    ["title"] = sessionTitle,           // 新增
    ["profile_id"] = profileId,         // 新增
    ["metadata"] = metaObj,
    ["last_consolidated"] = lastConsolidated
};
```

### 重命名内部类型

Agent 层的 `SessionInfo` 重命名为 `SessionFileInfo`（内部使用，表示文件信息）：

```csharp
// NanoBot.Agent/SessionManager.cs
public record SessionFileInfo(
    string Key,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    string Path,
    string? Title = null,
    string? ProfileId = null
);
```

---

## 5. WebUI 层改造方案 ✅

### 消除直接依赖 Agent 核心

**问题**: WebUI 直接注入 `ISessionManager`

**改造后**: WebUI 通过 `ISessionService` 访问会话管理，不直接依赖 Agent 核心的 `ISessionManager`。

```csharp
// WebUI/Services/SessionService.cs - 简化后
public class SessionService : ISessionService
{
    private readonly ISessionManager _sessionManager;
    private readonly IWorkspaceManager _workspace;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<SessionService> _logger;

    // 实现接口方法，直接操作 SessionManager
    // 不再使用 sessions_meta.json
}
```

---

## 6. 实施步骤

### 第一阶段：统一业务模型 ✅

1. [x] 在 `NanoBot.Core/Sessions/` 中定义统一模型
2. [x] Agent 层的 `SessionInfo` 重命名为 `SessionFileInfo`
3. [x] WebUI 使用 Core 层的模型

### 第二阶段：扩展 SessionManager ✅

1. [x] 扩展 metadata 结构，支持 `title` 和 `profile_id`
2. [x] 添加 `GetSessionTitle()` 和 `SetSessionTitle()` 等方法
3. [x] 添加 `GetSessionProfileId()` 和 `SetSessionProfileId()` 等方法

### 第三阶段：简化 WebUI ✅

1. [x] 修改 `Program.cs`，使用 `AddNanoBot()` 方法
2. [x] 删除 `sessions_meta.json` 相关代码
3. [x] 修改 `SessionService`，使用 Core 层的模型

### 第四阶段：Channels（无需修改）

Channels 是通过 CLI 命令运行的，已使用 CLI 的服务注册，无需单独简化。

### 第五阶段：清理 ✅

1. [x] 删除 WebUI 中的冗余模型定义（ISessionService.cs, IAgentService.cs）
2. [x] 确保所有会话数据存储在统一的 `.jsonl` 文件中

---

## 7. 文件变更清单

| 操作 | 文件 | 状态 |
|------|------|------|
| 新增 | `src/NanoBot.Core/Sessions/ISessionService.cs` | ✅ |
| 新增 | `src/NanoBot.Core/Sessions/IAgentService.cs` | ✅ |
| 新增 | `src/NanoBot.Core/Sessions/SessionInfo.cs` | ✅ |
| 新增 | `src/NanoBot.Core/Sessions/MessageInfo.cs` | ✅ |
| 新增 | `src/NanoBot.Core/Sessions/AttachmentInfo.cs` | ✅ |
| 修改 | `src/NanoBot.Agent/SessionManager.cs` | ✅ |
| 修改 | `src/NanoBot.WebUI/Program.cs` | ✅ |
| 修改 | `src/NanoBot.WebUI/Services/SessionService.cs` | ✅ |
| 修改 | `src/NanoBot.WebUI/Services/AgentService.cs` | ✅ |
| 修改 | `src/NanoBot.WebUI/Components/_Imports.razor` | ✅ |
| 修改 | `src/NanoBot.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | ✅ |
| 修改 | `src/NanoBot.Cli/Extensions/ServiceCollectionExtensions.cs` | ✅ |
| 删除 | `src/NanoBot.WebUI/Services/ISessionService.cs` | ✅ |
| 删除 | `src/NanoBot.WebUI/Services/IAgentService.cs` | ✅ |

---

## 8. 收益

1. **消除重复代码**: WebUI 直接使用 CLI 的服务注册方法
2. **统一会话存储**: 删除冗余的 `sessions_meta.json`
3. **统一业务模型**: 所有入口共享 Core 层的模型定义
4. **简化架构**: 无需创建新的 Host 项目
5. **降低维护成本**: 服务注册和模型定义只在一处维护
6. **统一会话**: CLI、WebUI、Channel 共享同一个会话
