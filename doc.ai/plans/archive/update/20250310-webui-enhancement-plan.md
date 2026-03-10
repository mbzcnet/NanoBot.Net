# NanoBot.WebUI 增强实现方案

## 文档信息

- **日期**: 2025-03-10
- **来源**: 基于 OpenCode WebUI 对比分析 ([详细报告](../../reports/update/20250310-webui-comparison.md))
- **源码参考**: OpenCode (https://github.com/opencode-ai/opencode)
- **优先级**: P1 (高优先级)
- **预计工期**: 4-6 周

---

## 0. OpenCode 架构分析

基于对 OpenCode 源码的深入分析，其 WebUI 采用以下技术架构：

### 0.1 技术栈

| 层级 | 技术 | 说明 |
|------|------|------|
| 框架 | SolidJS | 响应式 UI 框架，类似 React 但性能更好 |
| 状态管理 | SolidJS Store | 基于 Proxy 的细粒度状态管理 |
| 实时通信 | WebSocket | 原生 WebSocket 实现 |
| UI 组件 | Tailwind CSS + 自定义组件 | 原子化 CSS 方案 |
| 快捷键 | 自定义实现 | 支持 modifier key 检测 |
| 拖拽 | HTML5 Drag API | 原生拖拽事件处理 |

### 0.2 核心模块

```
packages/app/src/
├── context/           # 全局状态管理 (类似 Blazor 的 Scoped Services)
│   ├── sync.tsx      # 实时同步状态管理
│   ├── command.tsx   # 命令和快捷键系统
│   └── ...
├── components/        # UI 组件
│   ├── prompt-input/  # 输入框组件（含拖拽上传）
│   ├── settings-keybinds.tsx  # 快捷键设置
│   └── ...
└── pages/            # 页面
    └── session.tsx   # 会话页面
```

### 0.3 关键技术实现

#### 0.3.1 实时同步 (sync.tsx)

```typescript
// OpenCode 使用乐观更新 + WebSocket 同步
export const { use: useSync, provider: SyncProvider } = createSimpleContext({
  name: "Sync",
  init: () => {
    // 乐观更新：先更新本地状态，再同步到服务器
    const [optimistic, setOptimistic] = createStore({
      message: {},
      part: {}
    })

    // WebSocket 实时同步
    const syncMessage = async (sessionID: string, message: Message) => {
      // 1. 乐观添加到本地
      setOptimistic("message", sessionID, [...])
      // 2. 发送到服务器
      await api.syncMessage(sessionID, message)
    }
  }
})
```

#### 0.3.2 快捷键系统 (command.tsx)

```typescript
// OpenCode 的命令系统与快捷键深度集成
export function useCommand() {
  // 支持 modifier key 检测
  const mod = IS_MAC ? event.metaKey : event.ctrlKey

  // 快捷键格式: "mod+shift+p"
  // mod = Cmd (Mac) / Ctrl (Windows/Linux)

  // 快捷键分组
  const groups = {
    General: ["command.palette"],
    Session: ["session.new", "message.copy"],
    Navigation: ["file.open", "file.close"],
    // ...
  }
}
```

#### 0.3.3 拖拽上传 (drag-overlay.tsx)

```typescript
// OpenCode 的拖拽采用 CSS overlay 方式
export const PromptDragOverlay: Component = (props) => {
  return (
    <div class="absolute inset-0 z-10 flex items-center justify-center bg-surface-raised-stronger-non-alpha/90">
      <Icon name="photo" />
      <span>释放以上传文件</span>
    </div>
  )
}
```

---

## 1. 功能概述

基于与 OpenCode WebUI 的对比分析，确定以下 6 个核心增强功能：

| 功能 | 当前状态 | OpenCode 状态 | 优先级 | 参考 OpenCode 文件 |
|------|----------|---------------|--------|-------------------|
| 实时同步 | ❌ 缺失 | ✅ WebSocket 实时 | P0 | `context/sync.tsx`, `context/global-sync.tsx` |
| 密码保护 | ❌ 缺失 | ✅ 支持 | P1 | Server authentication in `context/server.tsx` |
| 过期时间 | ❌ 缺失 | ✅ 支持 | P1 | Session lifecycle management |
| 快捷键 | ❌ 缺失 | ✅ 丰富快捷键 | P1 | `context/command.tsx`, `settings-keybinds.tsx` |
| 拖拽上传 | ❌ 缺失 | ✅ 支持 | P1 | `prompt-input/drag-overlay.tsx` |
| 消息样式优化 | 🔧 基础 | ✅ 更美观 | P2 | `message-timeline.tsx` |

### 1.1 技术选型对比

| 功能 | OpenCode 实现 | NanoBot.WebUI 建议实现 |
|------|---------------|------------------------|
| 实时同步 | 原生 WebSocket + 乐观更新 | SignalR (已存在) + 乐观更新 |
| 快捷键 | 自定义事件监听 | Blazor 的 `@onkeydown` + JS interop |
| 拖拽上传 | HTML5 Drag API | Blazor `@ondrag*` 事件 |
| 状态管理 | SolidJS Store | Blazor 的 Scoped Services |
| 样式 | Tailwind CSS | MudBlazor + 自定义 CSS |

---

## 2. 实时同步 (Real-time Sync)

### 2.1 需求分析

OpenCode 使用原生 WebSocket 实现多客户端实时同步，其核心特点是：
- **乐观更新**: 先更新本地 UI，再同步到服务器
- **双向同步**: 服务器更新推送到所有客户端
- **会话隔离**: 每个会话独立的同步频道
- **自动重连**: 连接断开自动恢复

### 2.2 技术方案

当前项目使用 **SignalR**（已存在 `ChatHub.cs`），需要扩展以下功能：

#### 2.2.1 SignalR Hub 扩展

```csharp
// src/NanoBot.WebUI/Hubs/ChatHub.cs
public class ChatHub : Hub
{
    // 现有方法...

    // 新增：同步消息到所有连接的客户端
    public async Task SyncMessage(string sessionId, string messageId, string content, string role)
    {
        await Clients.Group($"session:{sessionId}").SendAsync("MessageSynced", messageId, content, role);
    }

    // 新增：通知客户端有新消息
    public async Task NotifyNewMessage(string sessionId, string messageId)
    {
        await Clients.Group($"session:{sessionId}").SendAsync("NewMessage", messageId);
    }

    // 新增：心跳检测
    public async Task Ping()
    {
        await Clients.Caller.SendAsync("Pong", DateTime.UtcNow);
    }
}
```

#### 2.2.2 Blazor 组件集成

```csharp
// Chat.razor 扩展
@inject IHubConnectionService HubService

@code {
    private HubConnection? _hubConnection;

    protected override async Task OnInitializedAsync()
    {
        // 初始化 SignalR 连接
        _hubConnection = await HubService.ConnectAsync();

        // 注册消息同步事件
        _hubConnection.On<string, string, string>("MessageSynced", (messageId, content, role) =>
        {
            // 更新本地消息状态
            UpdateMessageFromSync(messageId, content, role);
        });

        // 注册新消息通知
        _hubConnection.On<string>("NewMessage", (messageId) =>
        {
            // 从服务器获取新消息
            _ = LoadNewMessage(messageId);
        });

        // 加入会话组
        await _hubConnection.InvokeAsync("JoinSession", SessionId);
    }
}
```

#### 2.2.3 共享状态服务

```csharp
// src/NanoBot.WebUI/Services/SharedSessionState.cs
public interface ISharedSessionState
{
    event EventHandler<SessionMessageEventArgs> MessageReceived;
    event EventHandler<SyncStatusChangedEventArgs> SyncStatusChanged;

    Task ConnectAsync(string sessionId);
    Task DisconnectAsync();
    Task BroadcastMessageAsync(ChatMessage message);
    SyncStatus Status { get; }
}

public enum SyncStatus
{
    Disconnected,
    Connecting,
    Connected,
    Syncing,
    Error
}
```

### 2.3 实现步骤

1. **第 1 周**: 扩展 ChatHub 添加同步方法
2. **第 1 周**: 实现 SharedSessionState 服务
3. **第 2 周**: 在 Chat.razor 集成实时同步
4. **第 2 周**: 添加连接状态 UI 指示器
5. **第 2 周**: 实现自动重连逻辑

### 2.4 文件变更清单

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Hubs/ChatHub.cs` | 修改 | 添加同步相关方法 |
| `Services/SharedSessionState.cs` | 新增 | 共享状态服务 |
| `Services/HubConnectionService.cs` | 新增 | SignalR 连接管理 |
| `Components/Pages/Chat.razor` | 修改 | 集成实时同步 |
| `wwwroot/js/sync-client.js` | 新增 | 客户端同步逻辑 |

---

## 3. 密码保护 (Password Protection)

### 3.1 需求分析

参考 OpenCode 的 session sharing 设计，密码保护功能需要：
- 为分享的会话链接设置访问密码
- 密码使用 BCrypt 哈希存储
- 可选密码设置（不是所有分享都需要密码）
- 密码验证界面
- 安全的密码传输（HTTPS）

#### 3.1.1 OpenCode Session Sharing 参考

```typescript
// 来自 session-header.tsx 的分享功能
function useSessionShare(args: {
  globalSDK: ReturnType<typeof useGlobalSDK>
  currentSession: () => { share?: { url?: string } } | undefined
  sessionID: () => string | undefined
  projectDirectory: () => string
  platform: ReturnType<typeof usePlatform>
}) {
  // 分享状态管理
  const [state, setState] = createStore({
    share: false,
    unshare: false,
    copied: false,
    timer: undefined as number | undefined,
  })

  const shareUrl = createMemo(() => args.currentSession()?.share?.url)

  // 分享会话
  const shareSession = () => {
    args.globalSDK.client.session
      .share({ sessionID, directory: args.projectDirectory() })
      .then(() => { /* 生成分享链接 */ })
  }

  // 取消分享
  const unshareSession = () => {
    args.globalSDK.client.session
      .unshare({ sessionID, directory: args.projectDirectory() })
  }

  // 复制分享链接
  const copyLink = () => {
    navigator.clipboard.writeText(url)
  }
}
```

**核心特点：**
- 每个会话可以生成一个分享链接
- 分享链接可以通过密码保护
- 支持随时取消分享
- 分享状态持久化存储

### 3.2 技术方案

#### 3.2.1 密码存储模型

```csharp
// src/NanoBot.WebUI/Models/SessionProtection.cs
public class SessionProtection
{
    public string SessionId { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }  // BCrypt 哈希
    public DateTime? ExpiresAt { get; set; }
    public bool IsProtected => !string.IsNullOrEmpty(PasswordHash);
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
}
```

#### 3.2.2 密码保护服务

```csharp
// src/NanoBot.WebUI/Services/SessionProtectionService.cs
public interface ISessionProtectionService
{
    Task<bool> IsProtectedAsync(string sessionId);
    Task SetPasswordAsync(string sessionId, string password);
    Task RemovePasswordAsync(string sessionId);
    Task<bool> ValidatePasswordAsync(string sessionId, string password);
    Task<SessionProtection?> GetProtectionAsync(string sessionId);
}

public class SessionProtectionService : ISessionProtectionService
{
    private readonly IProtectedSessionStore _sessionStore;
    private readonly ILogger<SessionProtectionService> _logger;

    public async Task SetPasswordAsync(string sessionId, string password)
    {
        // 使用 BCrypt 哈希密码
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var protection = new SessionProtection
        {
            SessionId = sessionId,
            PasswordHash = hash,
            CreatedAt = DateTime.UtcNow
        };

        await _sessionStore.SetAsync($"protection:{sessionId}", protection);
    }

    public async Task<bool> ValidatePasswordAsync(string sessionId, string password)
    {
        var protection = await GetProtectionAsync(sessionId);
        if (protection?.PasswordHash == null) return true;

        return BCrypt.Net.BCrypt.Verify(password, protection.PasswordHash);
    }
}
```

#### 3.2.3 UI 组件

```razor
@* Components/Dialogs/PasswordDialog.razor *@
<MudDialog>
    <DialogContent>
        <MudText Typo="Typo.body1">@ContentText</MudText>
        <MudTextField @bind-Value="_password"
                      Label="密码"
                      InputType="InputType.Password"
                      Required="true"
                      RequiredError="请输入密码" />
        @if (_isSettingPassword)
        {
            <MudTextField @bind-Value="_confirmPassword"
                          Label="确认密码"
                          InputType="InputType.Password"
                          Required="true" />
        }
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">取消</MudButton>
        <MudButton Color="Color.Primary" OnClick="Submit">确定</MudButton>
    </DialogActions>
</MudDialog>
```

### 3.3 实现步骤

1. **第 1 周**: 添加 SessionProtection 模型
2. **第 1 周**: 实现 SessionProtectionService
3. **第 2 周**: 创建 PasswordDialog 组件
4. **第 2 周**: 在 NavMenu 添加密码设置菜单
5. **第 2 周**: 集成密码验证到 Chat 页面

### 3.4 文件变更清单

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Models/SessionProtection.cs` | 新增 | 密码保护模型 |
| `Services/SessionProtectionService.cs` | 新增 | 密码保护服务 |
| `Components/Dialogs/PasswordDialog.razor` | 新增 | 密码对话框 |
| `Components/Pages/Chat.razor` | 修改 | 集成密码验证 |
| `Components/Layout/NavMenu.razor` | 修改 | 添加密码设置菜单 |

---

## 4. 过期时间 (Expiration Time)

### 4.1 需求分析

参考 OpenCode 的会话生命周期管理，过期时间功能需要：
- 可选过期时间（1小时、1天、7天、30天、永不过期）
- 过期自动清理
- 过期提示界面
- 延长过期时间功能

#### 4.1.1 OpenCode Session Lifecycle 参考

OpenCode 通过以下方式管理会话生命周期：

```typescript
// OpenCode 会话数据结构参考
interface Session {
  id: string
  title?: string
  createdAt: number
  updatedAt: number
  share?: {
    url: string
    // 可以扩展添加过期时间字段
    expiresAt?: number
  }
  // 会话状态
  status: "active" | "archived" | "expired"
}

// 会话状态管理
const sessionStatus = createMemo(() => {
  const id = sessionID()
  if (!id) return { type: "idle" }
  return sync.data.session_status[id] ?? { type: "idle" }
})

// 工作流状态检测
const working = createMemo(() => {
  const pending = sessionMessages().findLast(
    (item): item is AssistantMessage =>
      item.role === "assistant" && typeof item.time.completed !== "number"
  )
  return !!pending || sessionStatus().type !== "idle"
})
```

**核心特点：**
- 会话有明确的生命周期状态（active/archived/expired）
- 支持会话分享链接过期控制
- 过期会话自动归档而不是删除
- 可以随时查看和恢复过期会话

### 4.2 技术方案

#### 4.2.1 过期配置模型

```csharp
// src/NanoBot.WebUI/Models/SessionExpiration.cs
public class SessionExpiration
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public ExpirationDuration Duration { get; set; }
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
    public bool NeverExpires => !ExpiresAt.HasValue;

    public TimeSpan? RemainingTime => ExpiresAt.HasValue
        ? ExpiresAt.Value - DateTime.UtcNow
        : null;
}

public enum ExpirationDuration
{
    OneHour,
    OneDay,
    SevenDays,
    ThirtyDays,
    Never
}
```

#### 4.2.2 过期清理服务

```csharp
// src/NanoBot.WebUI/Services/ExpirationCleanupService.cs
public class ExpirationCleanupService : BackgroundService
{
    private readonly ILogger<ExpirationCleanupService> _logger;
    private readonly ISessionService _sessionService;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredSessionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during expiration cleanup");
            }

            // 每小时检查一次
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task CleanupExpiredSessionsAsync()
    {
        var expiredSessions = await _sessionService.GetExpiredSessionsAsync();
        foreach (var session in expiredSessions)
        {
            await _sessionService.ArchiveSessionAsync(session.Id);
            _logger.LogInformation("Session {SessionId} archived due to expiration", session.Id);
        }
    }
}
```

#### 4.2.3 UI 组件

```razor
@* Components/Dialogs/ExpirationDialog.razor *@
<MudDialog>
    <DialogContent>
        <MudText Typo="Typo.h6" Class="mb-4">设置过期时间</MudText>
        <MudRadioGroup @bind-Value="_selectedDuration">
            <MudRadio Value="ExpirationDuration.OneHour">1 小时</MudRadio>
            <MudRadio Value="ExpirationDuration.OneDay">1 天</MudRadio>
            <MudRadio Value="ExpirationDuration.SevenDays">7 天</MudRadio>
            <MudRadio Value="ExpirationDuration.ThirtyDays">30 天</MudRadio>
            <MudRadio Value="ExpirationDuration.Never">永不过期</MudRadio>
        </MudRadioGroup>

        @if (_currentExpiration?.ExpiresAt != null)
        {
            <MudAlert Severity="Severity.Info" Class="mt-4">
                当前设置: @FormatExpiration(_currentExpiration.ExpiresAt.Value)
            </MudAlert>
        }
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">取消</MudButton>
        <MudButton Color="Color.Primary" OnClick="Submit">保存</MudButton>
    </DialogActions>
</MudDialog>
```

### 4.3 实现步骤

1. **第 1 周**: 添加 SessionExpiration 模型
2. **第 1 周**: 实现 ExpirationCleanupService
3. **第 2 周**: 创建 ExpirationDialog 组件
4. **第 2 周**: 在 SessionService 添加过期相关方法
5. **第 2 周**: 在 NavMenu 添加过期设置菜单

### 4.4 文件变更清单

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Models/SessionExpiration.cs` | 新增 | 过期配置模型 |
| `Services/ExpirationCleanupService.cs` | 新增 | 过期清理服务 |
| `Components/Dialogs/ExpirationDialog.razor` | 新增 | 过期设置对话框 |
| `Services/SessionService.cs` | 修改 | 添加过期相关方法 |
| `Program.cs` | 修改 | 注册后台服务 |

---

## 5. 快捷键 (Keyboard Shortcuts)

### 5.1 需求分析

OpenCode 提供丰富的快捷键支持，其核心设计：

#### 5.1.1 OpenCode 快捷键架构

```typescript
// 来自 context/command.tsx

// 1. 快捷键配置格式
//    "mod+shift+p" 表示 Cmd/Ctrl + Shift + P
//    "mod" 自动映射: Mac -> Cmd, Windows/Linux -> Ctrl

const IS_MAC = /(Mac|iPod|iPhone|iPad)/.test(navigator.platform)

// 2. 解析快捷键字符串
export function parseKeybind(config: string): Keybind[] {
  return config.split(",").map((combo) => {
    const parts = combo.trim().toLowerCase().split("+")
    const keybind: Keybind = {
      key: "",
      ctrl: false,
      meta: false,
      shift: false,
      alt: false,
    }

    for (const part of parts) {
      switch (part) {
        case "mod":  // mod = Cmd (Mac) / Ctrl (Win/Linux)
          if (IS_MAC) keybind.meta = true
          else keybind.ctrl = true
          break
        case "shift": keybind.shift = true; break
        case "alt": keybind.alt = true; break
        case "ctrl": keybind.ctrl = true; break
        default: keybind.key = part
      }
    }
  })
}

// 3. 匹配快捷键事件
export function matchKeybind(keybinds: Keybind[], event: KeyboardEvent): boolean {
  for (const kb of keybinds) {
    const keyMatch = kb.key === normalizeKey(event.key)
    const ctrlMatch = kb.ctrl === event.ctrlKey
    const metaMatch = kb.meta === event.metaKey
    const shiftMatch = kb.shift === event.shiftKey
    const altMatch = kb.alt === event.altKey

    if (keyMatch && ctrlMatch && metaMatch && shiftMatch && altMatch) {
      return true
    }
  }
  return false
}

// 4. 快捷键分组
const GROUPS = ["General", "Session", "Navigation", "Model and agent", "Terminal", "Prompt"]
```

#### 5.1.2 OpenCode 默认快捷键

| 快捷键 | 功能 | 命令 ID |
|--------|------|---------|
| `mod+shift+p` | 命令面板 | `command.palette` |
| `mod+k` | 聚焦输入框 | `prompt.focus` |
| `mod+n` | 新建会话 | `session.new` |
| `mod+shift+n` | 快速新建 | `session.quickNew` |
| `Escape` | 取消生成 | `session.cancel` |
| `mod+Enter` | 提交 | `prompt.submit` |
| `Shift+Enter` | 换行 | `prompt.newline` |
| `mod+Shift+K` | 清除会话 | `session.clear` |
| `mod+Shift+C` | 复制最后一条消息 | `message.copy` |

### 5.2 技术方案 (NanoBot.WebUI 实现)

#### 5.2.1 快捷键模型

#### 5.2.2 快捷键服务 (参考 OpenCode 设计)

```csharp
// src/NanoBot.WebUI/Services/KeyboardShortcutService.cs

public interface IKeyboardShortcutService
{
    // 注册快捷键 (格式: "Ctrl+Shift+N" 或 "mod+k")
    void Register(string keyCombo, ShortcutAction action);

    // 取消注册
    void Unregister(string keyCombo);

    // 检查快捷键是否被触发
    bool HandleKeyDown(KeyboardEventArgs args);

    // 格式化显示 (Mac显示⌘，Windows显示Ctrl)
    string FormatForDisplay(string keyCombo);

    // 持久化存储
    Task SaveCustomKeybindsAsync(Dictionary<string, string> keybinds);
    Task<Dictionary<string, string>> LoadCustomKeybindsAsync();
}

public class KeyboardShortcutService : IKeyboardShortcutService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly Dictionary<string, ShortcutAction> _shortcuts = new();
    private readonly ILogger<KeyboardShortcutService> _logger;

    // 默认快捷键配置 (参考 OpenCode)
    private static readonly Dictionary<string, string> DefaultKeybinds = new()
    {
        ["command.palette"] = "mod+shift+p",
        ["session.new"] = "mod+n",
        ["session.clear"] = "mod+shift+k",
        ["message.copy"] = "mod+shift+c",
        ["prompt.focus"] = "mod+k",
        ["prompt.submit"] = "mod+enter",
        ["prompt.newline"] = "shift+enter",
        ["session.cancel"] = "escape",
    };

    /// <summary>
    /// 解析快捷键字符串，支持跨平台 "mod" 键
    /// </summary>
    public static Shortcut ParseKeyCombo(string combo)
    {
        var parts = combo.ToLower().Split('+', StringSplitOptions.RemoveEmptyEntries);
        var shortcut = new Shortcut();

        foreach (var part in parts)
        {
            switch (part.Trim())
            {
                case "mod":  // mod = Cmd (Mac) / Ctrl (Win/Linux)
                    shortcut.IsMod = true;
                    break;
                case "ctrl":
                    shortcut.Ctrl = true;
                    break;
                case "shift":
                    shortcut.Shift = true;
                    break;
                case "alt":
                    shortcut.Alt = true;
                    break;
                default:
                    shortcut.Key = NormalizeKey(part);
                    break;
            }
        }

        return shortcut;
    }

    /// <summary>
    /// 匹配键盘事件
    /// </summary>
    public bool HandleKeyDown(KeyboardEventArgs args)
    {
        foreach (var (combo, action) in _shortcuts)
        {
            if (Matches(args, combo))
            {
                action.Invoke();
                return true;
            }
        }
        return false;
    }

    private bool Matches(KeyboardEventArgs args, string combo)
    {
        var shortcut = ParseKeyCombo(combo);

        // 检查修饰键
        bool modMatch = !shortcut.IsMod || (IsMac() ? args.MetaKey : args.CtrlKey);
        bool ctrlMatch = shortcut.Ctrl == args.CtrlKey;
        bool shiftMatch = shortcut.Shift == args.ShiftKey;
        bool altMatch = shortcut.Alt == args.AltKey;
        bool keyMatch = shortcut.Key == NormalizeKey(args.Key);

        return modMatch && ctrlMatch && shiftMatch && altMatch && keyMatch;
    }
}

public class Shortcut
{
    public string Key { get; set; } = "";
    public bool IsMod { get; set; }  // Cmd (Mac) / Ctrl (Win)
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
}
```

#### 5.2.3 JavaScript 辅助 (检测 Mac/Windows)

```javascript
// wwwroot/js/keyboard.js
window.nanoBotKeyboard = {
    // 检测是否为 Mac 系统
    isMac: function() {
        return /(Mac|iPod|iPhone|iPad)/.test(navigator.platform);
    },

    // 格式化快捷键显示
    formatKeybind: function(combo) {
        const isMac = this.isMac();
        const parts = combo.toLowerCase().split('+');

        return parts.map(part => {
            switch(part.trim()) {
                case 'mod': return isMac ? '⌘' : 'Ctrl';
                case 'ctrl': return isMac ? '⌃' : 'Ctrl';
                case 'shift': return isMac ? '⇧' : 'Shift';
                case 'alt': return isMac ? '⌥' : 'Alt';
                case 'enter': return '↵';
                case 'escape': return 'Esc';
                default: return part.charAt(0).toUpperCase() + part.slice(1);
            }
        }).join(isMac ? '' : '+');
    }
};
```

#### 5.2.4 Blazor 集成

```javascript
// wwwroot/js/shortcuts.js
window.nanoBotShortcuts = {
    dotNetRef: null,

    initialize: function(dotNetRef) {
        this.dotNetRef = dotNetRef;
        document.addEventListener('keydown', this.handleKeyDown.bind(this));
    },

    handleKeyDown: function(event) {
        // 忽略输入框中的快捷键（除非是特定组合）
        if (event.target.tagName === 'INPUT' || event.target.tagName === 'TEXTAREA') {
            if (!event.ctrlKey && !event.metaKey) return;
        }

        const modifiers = [];
        if (event.ctrlKey) modifiers.push('Ctrl');
        if (event.altKey) modifiers.push('Alt');
        if (event.shiftKey) modifiers.push('Shift');
        if (event.metaKey) modifiers.push('Meta');

        const key = event.key;

        // 触发 .NET 方法
        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('OnShortcutTriggered', key, modifiers.join(','));
        }

        // 内置快捷键处理
        this.handleBuiltInShortcuts(event, key, modifiers);
    },

    handleBuiltInShortcuts: function(event, key, modifiers) {
        const modString = modifiers.join(',');

        // Ctrl/Cmd + N: 新建会话
        if ((modString.includes('Ctrl') || modString.includes('Meta')) && key === 'n') {
            event.preventDefault();
            window.location.href = '/';
        }

        // Ctrl/Cmd + K: 搜索会话
        if ((modString.includes('Ctrl') || modString.includes('Meta')) && key === 'k') {
            event.preventDefault();
            // 触发搜索对话框
        }

        // Escape: 取消生成
        if (key === 'Escape' && modifiers.length === 0) {
            // 由 Blazor 组件处理
        }
    }
};
```

#### 5.2.5 快捷键设置界面 (参考 OpenCode)

```razor
@* Components/Pages/Settings.razor - 快捷键设置部分 *@
@inject IKeyboardShortcutService ShortcutService

<MudExpansionPanel Text="快捷键设置" Expanded="true">
    @foreach (var group in _shortcutGroups)
    {
        <MudText Typo="Typo.h6" Class="mt-4 mb-2">@group.Name</MudText>
        <MudList Dense="true">
            @foreach (var shortcut in group.Shortcuts)
            {
                <MudListItem>
                    <div class="d-flex align-center justify-space-between">
                        <div>
                            <MudText>@shortcut.Title</MudText>
                            <MudText Typo="Typo.caption" Color="Color.Secondary">
                                @shortcut.Description
                            </MudText>
                        </div>
                        <div class="d-flex align-center gap-2">
                            @if (_editingShortcut == shortcut.Id)
                            {
                                <MudTextField @bind-Value="_tempKeyCombo"
                                            Placeholder="按快捷键..."
                                            Variant="Variant.Outlined"
                                            Dense="true"
                                            Style="width: 150px"
                                            @onkeydown="CaptureKeyCombo"
                                            Immediate="true" />
                                <MudIconButton Icon="@Icons.Material.Filled.Check"
                                             Size="Size.Small"
                                             Color="Color.Success"
                                             OnClick="() => SaveKeyCombo(shortcut)" />
                                <MudIconButton Icon="@Icons.Material.Filled.Close"
                                             Size="Size.Small"
                                             OnClick="CancelEdit" />
                            }
                            else
                            {
                                <MudChip T="string" Variant="Variant.Outlined" Size="Size.Small">
                                    @FormatKeyCombo(shortcut.KeyCombo)
                                </MudChip>
                                <MudIconButton Icon="@Icons.Material.Filled.Edit"
                                             Size="Size.Small"
                                             OnClick="() => StartEdit(shortcut)" />
                            }
                        </div>
                    </div>
                </MudListItem>
            }
        </MudList>
    }
</MudExpansionPanel>

@code {
    private List<ShortcutGroup> _shortcutGroups = new()
    {
        new ShortcutGroup
        {
            Name = "通用",
            Shortcuts = new()
            {
                new() { Id = "command.palette", Title = "命令面板", KeyCombo = "mod+shift+p" },
                new() { Id = "settings.open", Title = "打开设置", KeyCombo = "mod+comma" },
            }
        },
        new ShortcutGroup
        {
            Name = "会话",
            Shortcuts = new()
            {
                new() { Id = "session.new", Title = "新建会话", KeyCombo = "mod+n" },
                new() { Id = "session.clear", Title = "清除会话", KeyCombo = "mod+shift+k" },
                new() { Id = "session.cancel", Title = "取消生成", KeyCombo = "escape" },
            }
        },
        new ShortcutGroup
        {
            Name = "消息",
            Shortcuts = new()
            {
                new() { Id = "message.copy", Title = "复制最后消息", KeyCombo = "mod+shift+c" },
                new() { Id = "prompt.focus", Title = "聚焦输入框", KeyCombo = "mod+k" },
            }
        },
        new ShortcutGroup
        {
            Name = "输入框",
            Shortcuts = new()
            {
                new() { Id = "prompt.submit", Title = "发送消息", KeyCombo = "mod+enter" },
                new() { Id = "prompt.newline", Title = "换行", KeyCombo = "shift+enter" },
            }
        }
    };

    private string? _editingShortcut;
    private string _tempKeyCombo = "";

    private string FormatKeyCombo(string combo)
    {
        // Mac: ⌘+⇧+N  Windows: Ctrl+Shift+N
        return KeyboardShortcutService.FormatForDisplay(combo);
    }

    private void CaptureKeyCombo(KeyboardEventArgs args)
    {
        // 捕获用户按下的快捷键组合
        var parts = new List<string>();

        var isMac = JSRuntime.InvokeAsync<bool>("nanoBotKeyboard.isMac").Result;

        if (isMac && args.MetaKey) parts.Add("mod");
        else if (!isMac && args.CtrlKey) parts.Add("mod");
        else if (args.CtrlKey) parts.Add("ctrl");

        if (args.ShiftKey) parts.Add("shift");
        if (args.AltKey) parts.Add("alt");

        if (!string.IsNullOrEmpty(args.Key) && args.Key.Length == 1)
        {
            parts.Add(args.Key.ToLower());
        }
        else if (args.Key is "Enter" or "Escape" or "ArrowUp" or "ArrowDown")
        {
            parts.Add(args.Key.ToLower());
        }

        _tempKeyCombo = string.Join("+", parts);
        args.PreventDefault();
    }
}
```

### 5.3 实现步骤

1. **第 1 周**: 实现 KeyboardShortcutService
2. **第 1 周**: 创建 shortcuts.js 客户端脚本
3. **第 2 周**: 在 MainLayout 初始化快捷键服务
4. **第 2 周**: 在 Chat.razor 添加快捷键处理
5. **第 2 周**: 创建设置界面管理快捷键

### 5.4 文件变更清单

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Services/KeyboardShortcutService.cs` | 新增 | 快捷键服务 |
| `wwwroot/js/shortcuts.js` | 新增 | 快捷键客户端脚本 |
| `Components/Layout/MainLayout.razor` | 修改 | 初始化快捷键 |
| `Components/Pages/Chat.razor` | 修改 | 添加快捷键处理 |
| `Components/Pages/Settings.razor` | 修改 | 快捷键设置界面 |

---

## 6. 拖拽上传 (Drag & Drop Upload)

### 6.1 需求分析

OpenCode 的拖拽上传设计简洁优雅：

#### 6.1.1 OpenCode 实现分析

```typescript
// prompt-input/drag-overlay.tsx
// 采用 CSS overlay 方式，当检测到拖拽进入时显示半透明遮罩

export const PromptDragOverlay: Component = (props) => {
  return (
    <Show when={props.type !== null}>
      <div class="absolute inset-0 z-10 flex items-center justify-center
                  bg-surface-raised-stronger-non-alpha/90 pointer-events-none">
        <div class="flex flex-col items-center gap-2 text-text-weak">
          <Icon name={props.type === "image" ? "photo" : "link"} class="size-8" />
          <span class="text-14-regular">{props.label}</span>
        </div>
      </div>
    </Show>
  )
}
```

**核心特点：**
- 使用 CSS overlay 显示拖拽提示
- `pointer-events-none` 确保事件穿透到底层
- 支持图片和 @mention 两种拖拽类型
- 简洁的视觉反馈

**支持的功能：**
- 拖拽文件到聊天区域
- 多文件同时上传
- 上传进度显示
- 支持图片、文档等多种类型

### 6.2 技术方案

#### 6.2.1 拖拽服务 (基于 OpenCode 设计)

```csharp
// src/NanoBot.WebUI/Services/DragDropService.cs
public interface IDragDropService
{
    event EventHandler<DragEventArgs> DragEnter;
    event EventHandler<DragEventArgs> DragOver;
    event EventHandler<DragEventArgs> DragLeave;
    event EventHandler<DropEventArgs> Drop;

    bool IsDragging { get; }
    Task InitializeAsync(ElementReference dropZone);
    Task HandleFilesAsync(IReadOnlyList<IBrowserFile> files);
}

public class DropEventArgs : EventArgs
{
    public IReadOnlyList<UploadFile> Files { get; set; } = new List<UploadFile>();
    public Point Position { get; set; }
}

public class UploadFile
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public Stream Content { get; set; } = Stream.Null;
    public double UploadProgress { get; set; }
    public UploadStatus Status { get; set; }
}
```

#### 6.2.2 拖拽上传组件

```razor
@* Components/Shared/DragDropZone.razor *@
<div @ref="_dropZone"
     class="drag-drop-zone @GetZoneClass()"
     @ondragenter="OnDragEnter"
     @ondragover="OnDragOver"
     @ondragleave="OnDragLeave"
     @ondrop="OnDrop">

    @if (_isDragging)
    {
        <div class="drag-overlay">
            <MudIcon Icon="@Icons.Material.Filled.CloudUpload" Size="Size.Large" />
            <MudText Typo="Typo.h6">释放以上传文件</MudText>
        </div>
    }

    @ChildContent

    @if (_uploadingFiles.Any())
    {
        <div class="upload-progress-panel">
            @foreach (var file in _uploadingFiles)
            {
                <div class="upload-item">
                    <MudText Typo="Typo.body2">@file.Name</MudText>
                    <MudProgressLinear Value="file.UploadProgress" Color="Color.Primary" />
                </div>
            }
        </div>
    }
</div>

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public EventCallback<IReadOnlyList<UploadFile>> OnFilesDropped { get; set; }
    [Parameter] public List<string> AllowedExtensions { get; set; } = new();

    private ElementReference _dropZone;
    private bool _isDragging;
    private List<UploadFile> _uploadingFiles = new();

    private async Task OnDrop(DragEventArgs args)
    {
        _isDragging = false;

        if (args.DataTransfer?.Files is { Count: > 0 } files)
        {
            var uploadFiles = new List<UploadFile>();
            foreach (var file in files)
            {
                uploadFiles.Add(new UploadFile
                {
                    Name = file.Name,
                    Size = file.Size,
                    ContentType = file.ContentType,
                    Content = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024)
                });
            }

            _uploadingFiles.AddRange(uploadFiles);
            await OnFilesDropped.InvokeAsync(uploadFiles);
        }
    }
}
```

#### 6.2.3 JavaScript 拖拽支持

```javascript
// wwwroot/js/dragdrop.js
window.nanoBotDragDrop = {
    initialize: function(dropZone, dotNetRef) {
        dropZone.addEventListener('dragenter', (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.add('drag-over');
            dotNetRef.invokeMethodAsync('OnDragEnter');
        });

        dropZone.addEventListener('dragover', (e) => {
            e.preventDefault();
            e.stopPropagation();
            e.dataTransfer.dropEffect = 'copy';
        });

        dropZone.addEventListener('dragleave', (e) => {
            e.preventDefault();
            e.stopPropagation();
            if (e.relatedTarget && !dropZone.contains(e.relatedTarget)) {
                dropZone.classList.remove('drag-over');
                dotNetRef.invokeMethodAsync('OnDragLeave');
            }
        });

        dropZone.addEventListener('drop', (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.remove('drag-over');

            const files = Array.from(e.dataTransfer.files);
            dotNetRef.invokeMethodAsync('OnFilesDropped', files.map(f => ({
                name: f.name,
                size: f.size,
                type: f.type
            })));
        });
    }
};
```

#### 6.2.4 CSS 样式

```css
/* wwwroot/css/dragdrop.css */
.drag-drop-zone {
    position: relative;
    min-height: 200px;
    border: 2px dashed transparent;
    border-radius: 8px;
    transition: all 0.2s ease;
}

.drag-drop-zone.drag-over {
    border-color: var(--mud-palette-primary);
    background-color: rgba(var(--mud-palette-primary-rgb), 0.05);
}

.drag-overlay {
    position: absolute;
    inset: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    background: rgba(0, 0, 0, 0.7);
    border-radius: 8px;
    z-index: 100;
}

.upload-progress-panel {
    position: fixed;
    bottom: 20px;
    right: 20px;
    width: 300px;
    background: var(--mud-palette-surface);
    border-radius: 8px;
    box-shadow: 0 4px 20px rgba(0, 0, 0, 0.3);
    padding: 16px;
    z-index: 1000;
}

.upload-item {
    margin-bottom: 8px;
}

.upload-item:last-child {
    margin-bottom: 0;
}
```

### 6.3 实现步骤

1. **第 2 周**: 实现 DragDropService
2. **第 2 周**: 创建 DragDropZone 组件
3. **第 3 周**: 创建 dragdrop.js 客户端脚本
4. **第 3 周**: 在 Chat.razor 集成拖拽上传
5. **第 3 周**: 添加 CSS 样式

### 6.4 文件变更清单

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Services/DragDropService.cs` | 新增 | 拖拽上传服务 |
| `Components/Shared/DragDropZone.razor` | 新增 | 拖拽上传区域组件 |
| `wwwroot/js/dragdrop.js` | 新增 | 拖拽客户端脚本 |
| `wwwroot/css/dragdrop.css` | 新增 | 拖拽样式 |
| `Components/Pages/Chat.razor` | 修改 | 集成拖拽上传 |

---

## 7. 消息样式优化 (Message Style Enhancement)

### 7.1 需求分析

参考 OpenCode 的 message-timeline.tsx 实现，消息样式优化需要：
- 代码块语法高亮增强（显示语言标签、复制按钮）
- 引用块样式优化（左侧彩色边框、背景色）
- 表格样式美化（表头背景、斑马纹）
- 链接卡片预览（自动提取网页标题和描述）
- 消息操作菜单（复制、引用、删除）
- 消息时间线分段显示（优化长对话性能）

#### 7.1.1 OpenCode Message Timeline 参考

```typescript
// 来自 pages/session/message-timeline.tsx

// 1. 消息时间线分段渲染（性能优化）
function createTimelineStaging(input: {
  sessionKey: () => string
  turnStart: () => number
  messages: () => UserMessage[]
  config: { init: number; batch: number }
}) {
  const [state, setState] = createStore({
    activeSession: "",
    completedSession: "",
    count: 0,
  })

  // 分批渲染消息，避免大量消息阻塞首屏
  const stagedCount = createMemo(() => {
    const total = input.messages().length
    if (input.turnStart() <= 0) return total
    if (state.completedSession === input.sessionKey()) return total
    const init = Math.min(total, input.config.init)
    if (state.count <= init) return init
    if (state.count >= total) return total
    return state.count
  })

  // 使用 requestAnimationFrame 分批加载
  const step = () => {
    count = Math.min(currentTotal, count + input.config.batch)
    setState("count", count)
    if (count >= currentTotal) {
      setState({ completedSession: sessionKey, activeSession: "" })
      return
    }
    frame = requestAnimationFrame(step)
  }
}

// 2. 消息工作流状态显示
const working = createMemo(() => {
  const pending = sessionMessages().findLast(
    (item): item is AssistantMessage =>
      item.role === "assistant" && typeof item.time.completed !== "number"
  )
  return !!pending || sessionStatus().type !== "idle"
})

// 3. 活跃消息追踪（用于滚动定位）
const activeMessageID = createMemo(() => {
  const parentID = pending()?.parentID
  if (parentID) {
    const message = messages.find((item) => item.id === parentID)
    if (message && message.role === "user") return message.id
  }
  // 返回最后一条用户消息
  for (let i = messages.length - 1; i >= 0; i--) {
    if (messages[i].role === "user") return messages[i].id
  }
})

// 4. 消息评论/标注支持
type MessageComment = {
  path: string
  comment: string
  selection?: { startLine: number; endLine: number }
}

const messageComments = (parts: Part[]): MessageComment[] =>
  parts.flatMap((part) => {
    if (part.type !== "text" || !part.synthetic) return []
    const next = readCommentMetadata(part.metadata) ?? parseCommentNote(part.text)
    if (!next) return []
    return [{ path: next.path, comment: next.comment, selection: next.selection }]
  })
```

**核心特点：**
- **分段渲染**: 大量消息时分批加载，优化首屏性能
- **工作流状态**: 清晰显示 AI 是否正在生成回复
- **活跃消息追踪**: 自动滚动到当前对话位置
- **评论支持**: 允许对消息添加评论和标注

### 7.2 技术方案

#### 7.2.1 Markdown 渲染增强

```csharp
// src/NanoBot.WebUI/Components/Shared/EnhancedMarkdownRenderer.razor
@using Markdig
@using Markdig.Extensions.SyntaxHighlighting

<div class="markdown-body enhanced-markdown">
    @((MarkupString)RenderMarkdown())
</div>

@code {
    [Parameter] public string Content { get; set; } = string.Empty;
    [Parameter] public bool EnableSyntaxHighlighting { get; set; } = true;
    [Parameter] public bool EnableLinkPreview { get; set; } = true;

    private string RenderMarkdown()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseSyntaxHighlighting()
            .UsePipeTables()
            .UseEmojiAndSmiley()
            .UseTaskLists()
            .Build();

        var html = Markdown.ToHtml(Content, pipeline);

        // 添加链接预览
        if (EnableLinkPreview)
        {
            html = AddLinkPreviews(html);
        }

        return html;
    }

    private string AddLinkPreviews(string html)
    {
        // 使用正则表达式查找 URL，添加预览卡片
        // 实际实现需要调用链接预览 API
        return html;
    }
}
```

#### 7.2.2 消息操作菜单

```razor
@* Components/Shared/MessageActions.razor *@
<div class="message-actions">
    <MudMenu Dense="true" AnchorOrigin="Origin.TopRight" TransformOrigin="Origin.TopRight">
        <ActivatorContent>
            <MudIconButton Icon="@Icons.Material.Filled.MoreHoriz" Size="Size.Small" />
        </ActivatorContent>
        <ChildContent>
            <MudMenuItem OnClick="CopyMessage">
                <MudIcon Icon="@Icons.Material.Filled.ContentCopy" Size="Size.Small" Class="mr-2" />
                复制
            </MudMenuItem>
            <MudMenuItem OnClick="CopyMarkdown">
                <MudIcon Icon="@Icons.Material.Filled.Code" Size="Size.Small" Class="mr-2" />
                复制 Markdown
            </MudMenuItem>
            <MudDivider />
            <MudMenuItem OnClick="QuoteMessage">
                <MudIcon Icon="@Icons.Material.Filled.FormatQuote" Size="Size.Small" Class="mr-2" />
                引用
            </MudMenuItem>
            <MudMenuItem OnClick="DeleteMessage" Disabled="!CanDelete">
                <MudIcon Icon="@Icons.Material.Filled.Delete" Size="Size.Small" Class="mr-2" Color="Color.Error" />
                删除
            </MudMenuItem>
        </ChildContent>
    </MudMenu>
</div>

@code {
    [Parameter] public ChatMessage Message { get; set; } = null!;
    [Parameter] public EventCallback<ChatMessage> OnQuote { get; set; }
    [Parameter] public EventCallback<ChatMessage> OnDelete { get; set; }
    [Parameter] public bool CanDelete { get; set; }

    private async Task CopyMessage()
    {
        await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", Message.Content);
        Snackbar.Add("已复制到剪贴板", Severity.Success);
    }
}
```

#### 7.2.3 增强 CSS 样式

```css
/* wwwroot/css/enhanced-messages.css */

/* 代码块样式 */
.enhanced-markdown pre {
    background: #1e1e1e;
    border-radius: 8px;
    padding: 16px;
    overflow-x: auto;
    position: relative;
}

.enhanced-markdown pre::before {
    content: attr(data-language);
    position: absolute;
    top: 0;
    right: 0;
    padding: 4px 8px;
    background: rgba(255, 255, 255, 0.1);
    border-radius: 0 8px 0 8px;
    font-size: 12px;
    color: #888;
}

/* 引用块样式 */
.enhanced-markdown blockquote {
    border-left: 4px solid var(--mud-palette-primary);
    background: rgba(var(--mud-palette-primary-rgb), 0.05);
    padding: 12px 16px;
    margin: 16px 0;
    border-radius: 0 8px 8px 0;
}

/* 表格样式 */
.enhanced-markdown table {
    width: 100%;
    border-collapse: collapse;
    margin: 16px 0;
}

.enhanced-markdown th,
.enhanced-markdown td {
    padding: 12px;
    border: 1px solid var(--mud-palette-divider);
    text-align: left;
}

.enhanced-markdown th {
    background: rgba(var(--mud-palette-primary-rgb), 0.1);
    font-weight: 600;
}

.enhanced-markdown tr:nth-child(even) {
    background: rgba(255, 255, 255, 0.02);
}

/* 消息操作菜单 */
.message-actions {
    opacity: 0;
    transition: opacity 0.2s ease;
}

.nb-message-bubble:hover .message-actions {
    opacity: 1;
}

/* 链接预览卡片 */
.link-preview-card {
    display: flex;
    border: 1px solid var(--mud-palette-divider);
    border-radius: 8px;
    overflow: hidden;
    margin: 8px 0;
    max-width: 500px;
}

.link-preview-image {
    width: 120px;
    height: 80px;
    object-fit: cover;
}

.link-preview-content {
    flex: 1;
    padding: 12px;
    display: flex;
    flex-direction: column;
}

.link-preview-title {
    font-weight: 600;
    margin-bottom: 4px;
}

.link-preview-description {
    font-size: 12px;
    color: var(--mud-palette-text-secondary);
    line-height: 1.4;
}
```

### 7.3 实现步骤

1. **第 3 周**: 创建 EnhancedMarkdownRenderer 组件
2. **第 3 周**: 创建 MessageActions 组件
3. **第 4 周**: 添加 enhanced-messages.css 样式
4. **第 4 周**: 在 Chat.razor 替换 MarkdownRenderer
5. **第 4 周**: 添加代码复制按钮

### 7.4 文件变更清单

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Components/Shared/EnhancedMarkdownRenderer.razor` | 新增 | 增强版 Markdown 渲染器 |
| `Components/Shared/MessageActions.razor` | 新增 | 消息操作菜单 |
| `wwwroot/css/enhanced-messages.css` | 新增 | 消息样式 |
| `Components/Pages/Chat.razor` | 修改 | 使用新渲染器 |
| `wwwroot/js/code-copy.js` | 新增 | 代码复制功能 |

---

## 8. 实施计划

### 8.1 开发排期

| 周次 | 功能 | 负责人 | 产出 |
|------|------|--------|------|
| 第 1 周 | 实时同步 (基础) + 密码保护 | TBD | ChatHub 扩展、密码服务 |
| 第 2 周 | 过期时间 + 快捷键 (基础) | TBD | 过期服务、快捷键服务 |
| 第 3 周 | 拖拽上传 + 快捷键 (完成) | TBD | 拖拽组件、快捷键配置 |
| 第 4 周 | 消息样式优化 | TBD | 增强渲染器、样式优化 |
| 第 5 周 | 集成测试 + Bug 修复 | TBD | 测试报告、修复补丁 |
| 第 6 周 | 文档完善 + 发布准备 | TBD | 用户文档、发布说明 |

### 8.2 依赖关系

```
实时同步
    └── SignalR Hub (已完成)

密码保护
    └── 实时同步 (可选依赖)

过期时间
    └── SessionService (已完成)

快捷键
    └── 独立实现

拖拽上传
    └── IFileStorageService (已完成)

消息样式优化
    └── MarkdownRenderer (已完成)
```

### 8.3 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| SignalR 连接稳定性 | 高 | 实现自动重连、降级到轮询 |
| 大文件上传性能 | 中 | 分片上传、进度显示 |
| 快捷键冲突 | 低 | 可配置、浏览器兼容性测试 |
| 样式兼容性 | 低 | MudBlazor 主题适配 |

---

## 9. 测试计划

### 9.1 单元测试

参考 OpenCode 的测试实践（位于 `packages/app/e2e/` 和 `packages/app/src/**/*.test.ts`）：

#### 9.1.1 服务层测试

```csharp
// tests/NanoBot.WebUI.Tests/Services/KeyboardShortcutServiceTests.cs
public class KeyboardShortcutServiceTests
{
    [Theory]
    [InlineData("mod+n", "Ctrl", "n", false)]  // Windows
    [InlineData("mod+n", "Meta", "n", true)]  // Mac
    [InlineData("ctrl+shift+k", "Ctrl,Shift", "k", false)]
    [InlineData("shift+enter", "Shift", "Enter", false)]
    public void ParseKeyCombo_ReturnsCorrectShortcut(string combo, string expectedMods, string expectedKey, bool isMac)
    {
        // Arrange & Act
        var shortcut = KeyboardShortcutService.ParseKeyCombo(combo);

        // Assert
        Assert.Equal(expectedKey, shortcut.Key);
        // 验证修饰键解析...
    }

    [Fact]
    public void HandleKeyDown_TriggersRegisteredShortcut()
    {
        // 测试快捷键触发逻辑
    }
}

// tests/NanoBot.WebUI.Tests/Services/SessionProtectionServiceTests.cs
public class SessionProtectionServiceTests
{
    [Fact]
    public async Task SetPasswordAsync_HashesPasswordCorrectly()
    {
        // 验证 BCrypt 哈希
    }

    [Fact]
    public async Task ValidatePasswordAsync_ReturnsTrue_ForCorrectPassword()
    {
        // 验证密码验证
    }

    [Fact]
    public async Task ValidatePasswordAsync_ReturnsFalse_ForIncorrectPassword()
    {
        // 验证密码错误拒绝
    }
}

// tests/NanoBot.WebUI.Tests/Services/ExpirationCleanupServiceTests.cs
public class ExpirationCleanupServiceTests
{
    [Fact]
    public async Task CleanupExpiredSessionsAsync_ArchivesExpiredSessions()
    {
        // 验证过期会话归档
    }

    [Fact]
    public async Task CleanupExpiredSessionsAsync_SkipsActiveSessions()
    {
        // 验证活跃会话不被处理
    }
}
```

#### 9.1.2 组件层测试 (bUnit)

```csharp
// tests/NanoBot.WebUI.Tests/Components/DragDropZoneTests.cs
public class DragDropZoneTests : TestContext
{
    [Fact]
    public void DragDropZone_DisplaysOverlay_WhenDragging()
    {
        // Arrange
        var cut = RenderComponent<DragDropZone>();

        // Act - 模拟拖拽进入
        cut.Find(".drag-drop-zone").DragEnter();

        // Assert
        cut.Find(".drag-overlay").Should().NotBeNull();
    }
}

// tests/NanoBot.WebUI.Tests/Components/MessageActionsTests.cs
public class MessageActionsTests : TestContext
{
    [Fact]
    public void MessageActions_CallsOnCopy_WhenCopyClicked()
    {
        // 测试消息操作菜单
    }
}
```

### 9.2 集成测试

#### 9.2.1 SignalR 实时同步测试

```csharp
// tests/NanoBot.WebUI.Integration.Tests/Hubs/ChatHubTests.cs
public class ChatHubTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task SyncMessage_BroadcastsToSessionGroup()
    {
        // 测试 SignalR 消息广播
        var connection = await BuildConnection();
        await connection.InvokeAsync("JoinSession", "test-session");

        var messageReceived = new TaskCompletionSource<bool>();
        connection.On<string, string, string>("MessageSynced", (id, content, role) =>
        {
            messageReceived.SetResult(true);
        });

        await connection.InvokeAsync("SyncMessage", "test-session", "msg-1", "Hello", "assistant");

        Assert.True(await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Reconnection_RestoresSessionState()
    {
        // 测试断线重连
    }
}
```

#### 9.2.2 API 集成测试

```csharp
// tests/NanoBot.WebUI.Integration.Tests/SessionSharingTests.cs
public class SessionSharingTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ShareSession_GeneratesShareUrl()
    {
        // 测试会话分享 API
    }

    [Fact]
    public async Task AccessSharedSession_WithPassword_ReturnsContent()
    {
        // 测试密码保护的分享链接
    }
}
```

### 9.3 E2E 测试 (Playwright)

参考 OpenCode 的 E2E 测试模式：

```csharp
// tests/NanoBot.WebUI.E2E.Tests/ShortcutsTests.cs
public class ShortcutsTests : PageTest
{
    [Fact]
    public async Task Shortcut_ModN_CreatesNewSession()
    {
        // 基于 OpenCode: e2e/commands/input-focus.spec.ts
        await Page.GotoAsync("/");
        await Page.Keyboard.DownAsync("Control");
        await Page.Keyboard.PressAsync("n");
        await Page.Keyboard.UpAsync("Control");

        await Expect(Page).ToHaveURLAsync("/session/*");
    }

    [Fact]
    public async Task Shortcut_Escape_CancelsGeneration()
    {
        // 基于 OpenCode: e2e/session/session.spec.ts
        await Page.GotoAsync("/session/test");
        await Page.Keyboard.PressAsync("Escape");

        // 验证取消状态
        await Expect(Page.Locator(".cancelled-indicator")).ToBeVisibleAsync();
    }
}

// tests/NanoBot.WebUI.E2E.Tests/DragDropTests.cs
public class DragDropTests : PageTest
{
    [Fact]
    public async Task DragDrop_UploadsFile()
    {
        // 基于 OpenCode: e2e/prompt/prompt-drop-file.spec.ts
        await Page.GotoAsync("/session/test");

        // 模拟文件拖拽
        var dataTransfer = await Page.EvaluateHandleAsync(@"() => {
            const dt = new DataTransfer();
            dt.items.add(new File(['test content'], 'test.txt', { type: 'text/plain' }));
            return dt;
        }");

        await Page.DispatchEventAsync(".drag-drop-zone", "drop", new { dataTransfer });

        await Expect(Page.Locator(".upload-success")).ToBeVisibleAsync();
    }
}

// tests/NanoBot.WebUI.E2E.Tests/RealtimeSyncTests.cs
public class RealtimeSyncTests : PageTest
{
    [Fact]
    public async Task RealtimeSync_DisplaysSyncedMessages()
    {
        // 打开两个浏览器窗口测试同步
        var page1 = await Browser.NewPageAsync();
        var page2 = await Browser.NewPageAsync();

        await page1.GotoAsync("/session/shared");
        await page2.GotoAsync("/session/shared");

        // page1 发送消息
        await page1.FillAsync("[data-testid='message-input']", "Hello from page1");
        await page1.ClickAsync("[data-testid='send-button']");

        // 验证 page2 收到消息
        await Expect(page2.Locator("text=Hello from page1")).ToBeVisibleAsync();
    }
}
```

### 9.4 浏览器兼容性测试

| 浏览器 | 版本 | 测试重点 |
|--------|------|----------|
| Chrome | Latest | 全功能测试 |
| Firefox | Latest | 快捷键、SignalR |
| Safari | Latest | 触摸事件、拖拽 |
| Edge | Latest | 企业环境测试 |

### 9.5 性能测试

```csharp
// tests/NanoBot.WebUI.Performance.Tests/MessageRenderingTests.cs
public class MessageRenderingTests
{
    [Fact]
    public void Render1000Messages_CompletesWithin2Seconds()
    {
        // 基于 OpenCode 的 timeline staging 性能测试
        var messages = Enumerable.Range(0, 1000)
            .Select(i => CreateMessage($"Message {i}"))
            .ToList();

        var stopwatch = Stopwatch.StartNew();
        // 渲染消息...
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 2000);
    }
}
```

---

## 10. 文档清单

### 10.1 需要更新的文档

| 文档 | 更新内容 |
|------|----------|
| `Feature-List.md` | 添加新功能状态 |
| `Configuration.md` | 添加新配置项说明 |
| `README.md` | 更新 WebUI 功能介绍 |

### 10.2 需要创建的新文档

- `WebUI-Shortcuts.md`: 快捷键使用指南
- `WebUI-Sharing.md`: 分享功能使用指南
- `WebUI-Security.md`: 安全功能说明

---

## 11. 成功指标

### 11.1 技术指标

| 指标 | 目标值 |
|------|--------|
| SignalR 连接成功率 | > 99% |
| 消息同步延迟 | < 100ms |
| 文件上传成功率 | > 99% |
| 页面加载时间 | < 2s |

### 11.2 用户体验指标

| 指标 | 目标值 |
|------|--------|
| 快捷键使用率 | > 60% |
| 拖拽上传使用率 | > 40% |
| 用户满意度 | > 4.0/5.0 |

---

*文档版本: 1.1*
*最后更新: 2025-03-10*

---

## 附录：OpenCode 源码参考

### A.1 关键文件映射

| NanoBot 功能 | OpenCode 参考文件 | 说明 |
|-------------|-------------------|------|
| 实时同步 | `packages/app/src/context/sync.tsx` | 乐观更新策略 |
| 快捷键 | `packages/app/src/context/command.tsx` | 快捷键解析与匹配 |
| 快捷键设置 | `packages/app/src/components/settings-keybinds.tsx` | UI 交互设计 |
| 拖拽上传 | `packages/app/src/components/prompt-input/drag-overlay.tsx` | CSS Overlay 实现 |
| 消息时间线 | `packages/app/src/pages/session/message-timeline.tsx` | 消息渲染优化 |
| 会话分享 | `packages/app/src/components/session/session-header.tsx` | 分享功能实现 |
| E2E 测试 | `packages/app/e2e/**/*.spec.ts` | 测试模式参考 |

### A.2 国际化参考

```typescript
// OpenCode 快捷键相关 i18n 文本（来自 zh.ts）
{
  "command.session.share": "分享会话",
  "command.session.share.description": "分享此会话并复制 URL 到剪贴板",
  "command.session.unshare": "停止分享会话",
  "command.session.unshare.description": "停止分享此会话",
  "session.share.popover.title": "发布到网络",
  "session.share.popover.description.shared": "此会话已发布到网络，任何人都可以通过链接访问。"
}
```
