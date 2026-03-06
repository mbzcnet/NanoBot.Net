# NanoBot.WebUI 代码审核报告

**审核日期**: 2026-03-03  
**审核范围**: `src/NanoBot.WebUI` 项目

---

## 执行摘要

本次审核重点关注 WebUI 项目是否遵循了"只作为 UI 级别、通道级别的组件，不允许任何 Agent 功能在 WebUI 项目中实现"的设计原则。

审核发现 WebUI 项目存在**严重的架构越权问题**，将大量本应属于 Agent 层或 Infrastructure 层的功能实现错误地放在了 WebUI 项目中。

| 严重程度 | 数量 |
|---------|------|
| 🔴 严重问题 | 4 |
| 🟠 设计缺陷 | 3 |
| 🟡 待确认 | 1 |

---

## 🔴 严重问题 (需要立即修复)

### 1. SessionService 直接依赖 ISessionManager 并实现会话管理

**文件**: `Services/SessionService.cs` (第 1-388 行)

**问题**: WebUI 项目直接注入了 `ISessionManager`（来自 `NanoBot.Agent` 命名空间），并实现了完整的会话管理逻辑：

```csharp
// 第 11 行 - 直接依赖 Agent 核心接口
private readonly ISessionManager _agentSessionManager;

// 第 48-50 行 - 直接调用 Agent 会话创建
var agentSession = _agentSessionManager.ListSessions()
    .FirstOrDefault(s => s.Key == sessionKey);

// 第 158 行 - 直接操作 Agent 会话
var agentSession = await _agentSessionManager.GetOrCreateSessionAsync(sessionKey);

// 第 185 行 - 直接创建 Agent 会话
await _agentSessionManager.GetOrCreateSessionAsync(sessionKey);

// 第 235 行 - 直接清除 Agent 会话
await _agentSessionManager.ClearSessionAsync(sessionKey);
```

**影响**: WebUI 层直接操作 Agent 核心组件，违反了分层架构原则。这使得：
- Agent 核心逻辑与 UI 耦合
- 难以独立测试 Agent 逻辑
- WebUI 无法替换为其他通道（如 Telegram、Discord）

**建议**: 将 `ISessionManager` 的访问封装为 WebUI 专属的接口，仅暴露 WebUI 需要的方法（如列出 WebUI 创建的会话），实现在 Infrastructure 层。

---

### 2. WebUI 层自行维护会话元数据存储

**文件**: `Services/SessionService.cs` (第 347-388 行)

**问题**: `SessionService` 实现了完整的会话元数据（`sessions_meta.json`）读写逻辑：

```csharp
// 第 14 行 - 自定义存储路径
private readonly string _sessionsMetaPath;

// 第 33 行 - 初始化路径
_sessionsMetaPath = Path.Combine(_workspace.GetSessionsPath(), "sessions_meta.json");

// 第 347-363 行 - 加载元数据
private async Task<Dictionary<string, SessionInfo>> LoadSessionsMetaAsync()

// 第 365-381 行 - 保存元数据
private async Task SaveSessionsMetaAsync(Dictionary<string, SessionInfo> sessionsData)
```

**影响**: 会话元数据应该由统一的服务管理，不应在 UI 层实现持久化逻辑。

**建议**: 将元数据管理移至 Infrastructure 层的独立服务。

---

### 3. SessionInfo、MessageInfo、AttachmentInfo 在 WebUI 层重复定义

**文件**: `Services/ISessionService.cs` (第 14-42 行)

**问题**: 这些 DTO 类应该在 Core 层定义，以便多个模块（CLI、WebUI、Channels）共享：

```csharp
// ISessionService.cs 第 14-42 行
public class SessionInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ProfileId { get; set; }
}

public class MessageInfo
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public List<AttachmentInfo> Attachments { get; set; } = new();
}

public class AttachmentInfo
{
    public string Id { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    // ...
}
```

**影响**:
- 无法在 CLI 和 WebUI 之间共享这些类型
- Agent 层的 `SessionInfo`（定义在 `SessionManager.cs`）与 WebUI 的 `SessionInfo` 命名冲突但结构不同

**建议**: 在 `NanoBot.Core` 层定义这些 DTO，确保所有模块使用一致的模型。

---

### 4. Program.cs 注册了所有 NanoBot 服务

**文件**: `Program.cs` (第 83-129 行)

**问题**: WebUI 项目不仅加载配置，还手动注册了所有 NanoBot 服务：

```csharp
// 第 98-103 行 - 注册所有 NanoBot 服务
builder.Services.AddMicrosoftAgentsAI(agentConfig.Llm);
builder.Services.AddNanoBotTools();
builder.Services.AddNanoBotContextProviders();
builder.Services.AddNanoBotInfrastructure(agentConfig.Workspace);
builder.Services.AddNanoBotBackgroundServices();
builder.Services.AddNanoBotAgent();
```

**影响**: 这使得 WebUI 项目实际上成为了"主机"项目，承担了 CLI 的职责。正确的做法应该是：
- WebUI 应该只依赖已经注册好的服务
- 配置和服务注册应该在独立的Host项目中完成

**建议**:
1. 创建独立的主机项目（如 `NanoBot.Host`）来组装所有服务
2. 或者让 WebUI 只通过代理调用远程 Agent 服务（微服务架构）

---

## 🟠 设计缺陷

### 5. AgentService 直接依赖 IAgentRuntime

**文件**: `Services/AgentService.cs` (第 10 行)

**问题**: WebUI 直接注入了 `IAgentRuntime`（Agent 核心运行时）：

```csharp
private readonly IAgentRuntime _agentRuntime;
```

**分析**: 这个问题相对不那么严重，因为 WebUI 确实需要调用 Agent 来处理消息。但从严格的分层角度看，可以考虑：
- 定义一个更高级别的接口（如 `IChatService`）在 Core 层
- 或者让 WebUI 通过 HTTP/gRPC 调用独立的 Agent 服务

**当前评估**: 可以接受，因为这是调用链路上的必要依赖。但建议审查是否所有 Agent 调用都必须经过 WebUI。

---

### 6. 文件存储服务直接暴露给 WebUI

**文件**: `Controllers/FilesController.cs`

**问题**: WebUI 直接注入了 `IFileStorageService` 和 `IWorkspaceManager`：

```csharp
private readonly IFileStorageService _fileStorageService;
private readonly IWorkspaceManager _workspaceManager;
```

**分析**: 文件服务是基础设施，WebUI 作为 UI 层需要访问文件系统是合理的（用于展示用户上传的文件）。这个问题可以接受。

---

### 7. ChatHub 实现过于简单

**文件**: `Hubs/ChatHub.cs`

**问题**: SignalR Hub 只是简单地转发消息，没有实际处理逻辑：

```csharp
public async Task SendMessage(string sessionId, string role, string content)
{
    await Clients.Group(sessionId).SendAsync("ReceiveMessage", role, content, DateTime.UtcNow);
}
```

**分析**: 这实际上是符合 WebUI 职责的——只是消息转发的通道。但需要确认前端是否能正确处理这些消息。

---

## 🟡 待确认

### 8. 会话文件路径硬编码

**文件**: `Services/SessionService.cs` (第 51 行)

```csharp
if (File.Exists(Path.Combine(_workspace.GetSessionsPath(), $"{sessionKey}.jsonl")))
```

**观察**: 代码同时检查了 Agent SessionManager 创建的文件和自定义的 `sessions_meta.json`。这种混合方式容易造成混乱。

---

## 架构建议

### 理想的分层架构

```
┌─────────────────────────────────────────────────────────┐
│                    WebUI (UI Layer)                     │
│  - 页面组件 (Razor Pages)                                │
│  - SignalR Hub (实时通信)                               │
│  - 文件控制器 (静态资源)                                 │
└─────────────────────┬───────────────────────────────────┘
                      │ 调用
                      ▼
┌─────────────────────────────────────────────────────────┐
│               Core/Interfaces (抽象层)                   │
│  - IChatService / IWebUIService                        │
│  - SessionInfo, MessageInfo, AttachmentInfo (DTOs)     │
└─────────────────────┬───────────────────────────────────┘
                      │ 实现
                      ▼
┌─────────────────────────────────────────────────────────┐
│            Infrastructure (基础设施层)                   │
│  - WebUISessionService: 实现会话管理                    │
│  - 元数据存储服务                                        │
└─────────────────────┬───────────────────────────────────┘
                      │ 访问
                      ▼
┌─────────────────────────────────────────────────────────┐
│            Agent (核心层)                                │
│  - ISessionManager                                     │
│  - IAgentRuntime                                        │
└─────────────────────────────────────────────────────────┘
```

### 修复步骤建议

1. **第一步**: 将 `SessionInfo`, `MessageInfo`, `AttachmentInfo` 移至 `NanoBot.Core`

2. **第二步**: 在 `NanoBot.Core` 创建 `IWebUIService` 接口：
   ```csharp
   public interface IWebUIService
   {
       Task<List<SessionInfo>> GetSessionsAsync();
       Task<SessionInfo> CreateSessionAsync(string? title = null, string? profileId = null);
       // ...
   }
   ```

3. **第三步**: 在 `NanoBot.Infrastructure` 创建 `WebUIService` 实现

4. **第四步**: 重构 `SessionService`，使其成为简单的 UI 辅助类

5. **第五步**: 考虑抽取主机逻辑到独立项目

---

## 总结

WebUI 项目存在严重的架构越权问题，主要体现在：

1. ❌ 直接依赖并操作 Agent 核心组件 (`ISessionManager`)
2. ❌ 自行实现会话元数据的持久化
3. ❌ 重复定义应在 Core 层共享的 DTO
4. ❌ 承担了服务注册和组装的主机职责

这些问题违反了分层架构的基本原则，使得 WebUI 与 Agent 核心逻辑过度耦合。建议尽快按照上述修复步骤进行重构。

