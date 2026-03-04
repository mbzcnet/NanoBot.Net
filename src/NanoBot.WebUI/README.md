# NanoBot.WebUI

`NanoBot.WebUI` 是 NanoBot.Net 的 Web 前端，基于 ASP.NET Core 8 + Blazor Server + MudBlazor。

它提供会话管理、流式聊天、配置编辑、渠道配置和基础文件访问能力。

## 技术栈

- .NET 8 (`net8.0`)
- ASP.NET Core Razor Components (Interactive Server)
- MudBlazor
- SignalR (`/hub/chat`)
- Blazored.LocalStorage

## 功能概览

- 聊天会话管理：新建、重命名、删除、分页加载
- 流式聊天：逐块显示 Agent 输出，支持中断生成
- 图片输入：支持上传/粘贴图片并在消息中引用
- 配置页：`/config`、`/config/profiles*`、`/config/channels`
- 设置页：`/settings`（语言设置可写入本地存储）
- 文件访问接口：`GET /api/files/sessions/{**relativePath}`
- 本地化：`zh-CN`、`en-US`

## 目录结构

```text
src/NanoBot.WebUI/
├── Components/          # 页面、布局、共享组件
├── Controllers/         # API 控制器（文件访问）
├── Hubs/                # SignalR Hub
├── Middleware/          # 异常处理中间件
├── Resources/           # 本地化资源
├── Services/            # 会话、Agent、认证等服务
├── wwwroot/             # 静态资源
├── Program.cs           # 应用入口
└── appsettings.json     # 默认配置
```

## 启动方式

在仓库根目录执行。

### 方式 1：通过 CLI（推荐）

```bash
dotnet run --project src/NanoBot.Cli -- webui
```

常用参数：

- `--port <端口>`：指定监听端口
- `--config <配置文件路径>`：指定 NanoBot 配置文件
- `--no-browser`：不自动打开浏览器

### 方式 2：直接运行 WebUI 项目

```bash
dotnet run --project src/NanoBot.WebUI -- --config <配置文件路径>
```

可选参数：

- `--config` / `-c`：配置文件路径
- `--urls`：覆盖监听地址，例如 `--urls http://0.0.0.0:18888`

## 配置说明

`Program.cs` 会优先使用 `--config` 参数加载 Agent 配置；未找到时尝试自动解析已有配置。

`src/NanoBot.WebUI/appsettings.json` 中包含 WebUI 默认配置：

```json
{
  "WebUI": {
    "Server": {
      "Port": 18888,
      "Urls": "http://0.0.0.0:18888"
    },
    "Auth": {
      "Mode": "token",
      "Token": "nanobot-dev-token",
      "AllowLocalhost": true
    },
    "Cors": {
      "AllowedOrigins": [
        "http://localhost:18888",
        "http://127.0.0.1:18888"
      ]
    }
  }
}
```

## 路由

- `/`：首页
- `/chat/{SessionId}`：聊天页
- `/config`：配置主页
- `/config/profiles`：配置档案列表
- `/config/profiles/new`：新建配置档案
- `/config/profiles/edit/{ProfileName}`：编辑配置档案
- `/config/channels`：渠道配置
- `/settings`：WebUI 设置

## 开发说明

- 依赖项目：`NanoBot.Core`、`NanoBot.Agent`、`NanoBot.Cli`、`NanoBot.Infrastructure`
- 使用 CORS 默认策略，来源由 `WebUI:Cors:AllowedOrigins` 控制
- 启用了全局友好异常中间件：API 返回 JSON，页面返回 HTML 错误页

## 已知限制

- `Settings.razor` 中多数设置项仍是 UI 层状态，尚未完整持久化到配置文件
- `AuthService` 已提供 token/password 校验能力，但认证流程集成仍需结合上层入口逻辑验证

