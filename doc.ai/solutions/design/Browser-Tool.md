# Browser Tool 实现方案设计

## 1. 背景与目标

在移植 OpenClaw 到 NanoBot.Net 的过程中，浏览器控制（Browser Tool）是 Agent 获取外部信息和执行网页交互的核心能力。通过分析 OpenClaw 的源码（`src/browser/`），其底层基于 **Playwright**，通过一套封装好的服务来实现浏览器的启动、页面管理、DOM 树快照（Snapshot）和页面交互（Act）。

**本方案的目标**是在 NanoBot.Net 中基于 `.NET 8` 和 `Microsoft.Playwright` 实现一套功能对标 OpenClaw 的 Browser Tool。

## 2. 核心架构设计

为了符合 NanoBot.Net 的架构原则（接口隔离、依赖注入），我们将浏览器工具拆分为以下几个层级：

1. **Tool 层 (`BrowserTool`)**: 
   - 继承或实现 `Microsoft.Agents.AI` 相关的工具接口。
   - 暴露唯一的 `browser` 工具给 LLM，接收 `action`（如 status, open, snapshot, act 等）及相关参数。
2. **Service 层 (`IBrowserService`)**: 
   - 负责具体的业务逻辑调度。
   - 提供独立的方法对应各类 action（如 `GetSnapshotAsync`, `ExecuteActionAsync`）。
3. **Playwright 驱动层 (`IPlaywrightManager`)**: 
   - 封装 `Microsoft.Playwright` 的核心 API。
   - 负责 CDP 连接、浏览器实例生命周期管理、Context 和 Page 的缓存与状态维护。

## 3. 核心概念与对标机制

### 3.1 配置文件与多 Profile 支持
对标 OpenClaw 的 `profile` 机制，支持以下两种模式：
- **`openclaw` (默认独立模式)**: 使用 Playwright 启动一个独立的无头/有头浏览器实例，与用户日常浏览器隔离。
- **`chrome` (CDP 转发模式/扩展模式)**: 连接到现有的本地浏览器（通过 `connectOverCDP` 或 WebSocket），实现在用户的现有 Tab 上操作。

### 3.2 页面快照 (Snapshot & Refs)
这是 Agent 理解网页的基石。OpenClaw 使用了 `AI Snapshot` 和 `Role Snapshot`。
- **解析页面树**: 使用 Playwright 获取页面的 Accessibility Tree 或 DOM 结构。
- **注入 Ref ID**: 给可交互元素（button, link, input）分配短数字 ID（如 `12`, `e12`），并将此 ID 随文本结构返回给大模型。
- **元素寻址**: 当 LLM 返回 `click 12` 时，系统在缓存的快照映射中找到对应的 Playwright `Locator` 并执行点击。

### 3.3 交互动作 (Actions)
统一处理 `click`, `type`, `scroll`, `hover`, `evaluate` 等命令，基于 `Snapshot` 生成的 `Ref ID` 定位元素并执行操作。

## 4. 接口与类设计定义 (草案)

**注意：此部分仅为基础签名定义，实际实现放置于独立项目中。**

### 4.1 服务接口定义 (`NanoBot.Core/Tools/Browser`)

```csharp
namespace NanoBot.Core.Tools.Browser
{
    /// <summary>
    /// 浏览器基础动作请求
    /// </summary>
    public class BrowserToolRequest
    {
        public string Action { get; set; } // status, open, tabs, close, snapshot, screenshot, navigate, act
        public string Profile { get; set; } // default: "openclaw"
        public string TargetId { get; set; } // Page/Tab ID
        public string TargetUrl { get; set; }
        
        // Snapshot 特有参数
        public string SnapshotFormat { get; set; } // ai, aria
        
        // Act 特有参数
        public BrowserActionRequest Request { get; set; } 
    }

    public class BrowserActionRequest
    {
        public string Kind { get; set; } // click, type, scroll, wait 等
        public string Ref { get; set; }  // Snapshot 映射出的目标元素 ID
        public string Text { get; set; } // Type 时的输入文本
    }

    /// <summary>
    /// 浏览器服务抽象
    /// </summary>
    public interface IBrowserService
    {
        Task<object> GetStatusAsync(string profile);
        Task StartAsync(string profile);
        Task StopAsync(string profile);
        Task<IEnumerable<object>> GetTabsAsync(string profile);
        Task<object> OpenTabAsync(string url, string profile);
        Task FocusTabAsync(string targetId, string profile);
        Task CloseTabAsync(string targetId, string profile);
        
        // 核心：快照生成
        Task<object> GetSnapshotAsync(string targetId, string format, string profile);
        // 核心：UI 操作
        Task<object> ExecuteActionAsync(BrowserActionRequest request, string targetId, string profile);
    }
}
```

### 4.2 Playwright 驱动层抽象 (`NanoBot.Infrastructure`)

```csharp
namespace NanoBot.Infrastructure.Browser
{
    /// <summary>
    /// 管理 Playwright 实例、Browser 和 Page 的连接池
    /// </summary>
    public interface IPlaywrightSessionManager
    {
        Task<IBrowser> ConnectBrowserAsync(string profile);
        Task<IPage> GetPageByTargetIdAsync(string targetId, string profile);
        Task<IPage> CreatePageAsync(string url, string profile);
        Task CloseBrowserAsync(string profile);
    }
}
```

## 5. 技术选型与依赖

- **NuGet 包**: `Microsoft.Playwright` (官方 .NET Playwright SDK)
- **依赖注入**: 在 `NanoBot.Infrastructure` 中实现 `IPlaywrightSessionManager` 并注入。在 `NanoBot.Tools` 中实现 `IBrowserService` 和具体的 Tool。
- **安全性**: 
  - 通过 SSRF 检查拦截局域网 IP（除非在配置中显式允许）。
  - 执行 `Evaluate` 脚本时进行沙箱隔离。

## 6. 开发与实施计划

1. **基础设施引入 (Phase 1)**
   - 引入 `Microsoft.Playwright` 到 `NanoBot.Infrastructure`。
   - 实现 `PlaywrightSessionManager`，支持无头浏览器的启动和 CDP 连接机制。
   
2. **核心业务实现 (Phase 2)**
   - 实现 `BrowserService`，封装 Tabs 管理（打开、关闭、列表）。
   - 实现 `Snapshot` 逻辑，遍历 `IPage` 的 DOM 节点，提取可交互元素，生成文本版页面快照并建立 `Ref ID` 映射。

3. **操作映射实现 (Phase 3)**
   - 实现 `ActionExecutor`，处理从 `Ref ID` 到 `Locator` 的转换。
   - 实现具体的点击、输入、滚动等逻辑。

4. **Agent 工具集成 (Phase 4)**
   - 在 `NanoBot.Tools` 中创建 `BrowserTool`，桥接 `Microsoft.Agents.AI` 和 `IBrowserService`。
   - 编写单元测试和端到端测试。

## 7. 实现状态更新（2026-02-28）

当前 NanoBot.Net Browser Tool 已具备以下能力：

- `action`: `status`, `start`, `stop`, `tabs`, `open`, `navigate`, `close`, `snapshot`, `content`, `act`
- `act.kind`: `click`, `type`, `press`, `wait`, `scroll`
- `content`: 面向 Agent 的页面正文读取入口（支持 `selector` 与 `maxChars`）

推荐调用工作流：

1. `open`/`navigate` 打开目标页面
2. `snapshot` 获取可交互 refs
3. `act` 执行点击/输入/等待/滚动
4. `content` 提取当前页面文本并总结

说明：

- 当前版本优先保障 `agent -m "打开百度，然后浏览最新新闻，然后告诉我最新的新闻内容"` 这类任务的可执行闭环。
- 后续可继续按需对齐 OpenClaw 的高级能力（如 `screenshot`、`console`、`pdf`、`upload`、`dialog` 等）。
