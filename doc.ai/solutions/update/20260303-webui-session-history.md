# WebUI 历史会话功能实现方案

**日期**: 2026-03-03

## 概述

本文档描述 WebUI 侧边栏历史会话列表的显示、切换、删除功能的实现方案。

## 现状分析

### 当前代码结构

1. **SessionService** (`/src/NanoBot.WebUI/Services/SessionService.cs`)
   - `GetSessionsAsync()`: 获取会话列表
   - `GetMessagesAsync(string sessionId)`: 获取会话消息
   - `CreateSessionAsync()`: 创建新会话
   - `DeleteSessionAsync(string sessionId)`: 删除会话
   - 基于文件系统的会话管理

2. **NavMenu** (`/src/NanoBot.WebUI/Components/Layout/NavMenu.razor`)
   - 使用 5 秒定时器刷新会话列表
   - 渲染会话标题列表
   - 提供新建会话按钮

3. **Chat.razor** (`/src/NanoBot.WebUI/Components/Pages/Chat.razor`)
   - 通过 URL 参数 `/chat/{SessionId}` 访问会话
   - 从 SessionService 加载消息

4. **附件存储** (`/src/NanoBot.Infrastructure/Storage/FileStorageService.cs`)
   - 图片保存在 `sessions/{sessionId}/uploads/` 目录
   - 支持文件删除和读取

### 现有问题

1. **会话持久化时机**
   - `SessionService.CreateSessionAsync()` 仅创建内存会话
   - 会话文件可能在首次消息后才创建

2. **UI 交互问题**
   - NavMenu 中没有删除按钮
   - 没有当前会话高亮显示
   - 没有会话时间的友好显示

3. **会话标题**
   - 新建会话时没有消息，标题使用默认格式
   - 不支持用户自定义标题

## 设计方案

### 1. 数据模型设计

#### 1.1 会话信息模型

```csharp
public class SessionInfo
{
    public string Id { get; set; }           // 会话ID
    public string Title { get; set; }         // 会话标题
    public DateTime CreatedAt { get; set; }    // 创建时间
    public DateTime UpdatedAt { get; set; }    // 更新时间
    public string? ProfileId { get; set; }    // 使用的配置Profile
}
```

#### 1.2 消息模型

```csharp
public class MessageInfo
{
    public string Id { get; set; }            // 消息ID
    public string SessionId { get; set; }     // 所属会话ID
    public string Role { get; set; }          // 角色 (user/assistant)
    public string Content { get; set; }       // 消息内容
    public DateTime Timestamp { get; set; }   // 时间戳
    public List<AttachmentInfo> Attachments { get; set; }  // 附件列表
}
```

#### 1.3 附件模型

```csharp
public class AttachmentInfo
{
    public string Id { get; set; }            // 附件ID
    public string MessageId { get; set; }     // 所属消息ID
    public string FileName { get; set; }      // 文件名
    public string FileType { get; set; }      // 文件类型 (image/audio/document)
    public string RelativePath { get; set; }   // 相对路径
    public long FileSize { get; set; }         // 文件大小
}
```

### 2. 接口设计

#### 2.1 会话服务接口

```csharp
public interface ISessionService
{
    // 获取所有会话列表
    Task<List<SessionInfo>> GetSessionsAsync();

    // 获取单个会话信息
    Task<SessionInfo?> GetSessionAsync(string sessionId);

    // 创建新会话
    Task<SessionInfo> CreateSessionAsync(string? title = null, string? profileId = null);

    // 重命名会话
    Task RenameSessionAsync(string sessionId, string newTitle);

    // 删除会话 (包括消息、附件)
    Task DeleteSessionAsync(string sessionId);

    // 获取会话消息
    Task<List<MessageInfo>> GetMessagesAsync(string sessionId);

    // 添加消息
    Task<MessageInfo> AddMessageAsync(string sessionId, string role, string content, List<AttachmentInfo>? attachments = null);
}
```

### 3. 存储方案

使用 JSON 文件存储会话元数据，会话消息继续使用现有的 JSONL 文件格式。

#### 3.1 存储结构

```
{Workspace}/sessions/
├── sessions_meta.json              # 会话元数据 (JSON格式)
├── {sessionId}/
│   ├── uploads/
│   │   ├── {timestamp}_{guid}_image.png
│   │   ├── {timestamp}_{guid}_audio.mp3
│   │   └── {timestamp}_{guid}_document.pdf
│   └── webui:{sessionId}.jsonl    # 消息历史
└── ...
```

#### 3.2 sessions_meta.json 结构

```json
{
  "session_id_1": {
    "id": "session_id_1",
    "title": "会话标题",
    "createdAt": "2026-03-03T10:00:00",
    "updatedAt": "2026-03-03T10:30:00",
    "profileId": null
  },
  "session_id_2": {
    "id": "session_id_2",
    "title": "另一个会话",
    "createdAt": "2026-03-02T15:00:00",
    "updatedAt": "2026-03-02T16:00:00",
    "profileId": "default"
  }
}
```

### 4. 附件存储方案

继续使用现有的本地文件存储方案，附件元数据存储在 JSON 中。

#### 4.1 文件管理策略

1. **图片**: 上传时保存到 `sessions/{sessionId}/uploads/`
2. **语音**: 语音消息同样保存到 uploads 目录
3. **文档**: 支持 PDF、DOC 等文档格式
4. **删除级联**: 删除会话时，同时删除本地文件

#### 4.2 接口扩展

```csharp
public interface IFileStorageService
{
    // 现有方法...
    
    // 新增: 删除会话目录下的所有文件
    Task<bool> DeleteSessionDirectoryAsync(string sessionId, CancellationToken cancellationToken = default);
}
```

### 5. 会话标题方案

#### 5.1 标题生成规则

1. **新建会话时**: 使用默认格式 "会话 {MM-dd HH:mm}"
2. **首次消息后**: 自动更新为用户第一条消息的前30个字符
3. **用户自定义**: 允许用户随时重命名

#### 5.2 标题更新时机

- **自动提取**: 在 `AddMessageAsync` 中检测是否是该会话的第一条用户消息
- **手动修改**: 用户点击编辑按钮时触发

### 6. UI 增强设计

#### 6.1 侧边栏布局

```
┌─────────────────────────┐
│    [新建会话按钮]        │
├─────────────────────────┤
│  最近会话                │
├─────────────────────────┤
│ 📄 会话标题A    ✏️ 🗑️   │  ← 当前会话高亮
│    3分钟前              │
├─────────────────────────┤
│ 📄 会话标题B    ✏️ 🗑️   │
│    1小时前              │
├─────────────────────────┤
│ 📄 会话标题C    ✏️ 🗑️   │
│    昨天                 │
└─────────────────────────┘
```

#### 6.2 交互行为

| 操作 | 触发方式 | 响应 |
|-----|---------|------|
| 创建会话 | 点击按钮 | 跳转新会话页面 |
| 切换会话 | 点击会话项 | 导航到对应聊天页 |
| 删除会话 | 点击删除图标 | 确认对话框后删除 |
| 重命名 | 点击编辑图标 | 弹出编辑框 |
| 刷新 | 定时/手动 | 重新加载会话列表 |

#### 6.3 当前会话高亮

- 通过 URL 参数 `/chat/{SessionId}` 识别当前会话
- 添加 `active` CSS 类高亮显示
- 使用 `LocationChanged` 事件监听导航变化

## 实现步骤

### 步骤 1: 数据层改造 (P0) ✅

1. ✅ 创建 `sessions_meta.json` 存储会话元数据
2. ✅ 实现 `ISessionService` 文件版本
3. ✅ 添加会话持久化逻辑

### 步骤 2: 文件存储增强 (P0) ✅

1. ✅ 扩展 `IFileStorageService` 接口
2. ✅ 实现会话目录批量删除

### 步骤 3: NavMenu UI 增强 (P0) ✅

1. ✅ 添加当前会话高亮
2. ✅ 添加删除按钮和确认对话框
3. ✅ 添加时间格式化显示

### 步骤 4: 会话标题功能 (P1) ✅

1. ✅ 实现自动标题提取
2. ✅ 实现用户自定义标题
3. ✅ UI 交互完善

## 相关文件清单

| 文件路径 | 操作 | 说明 |
|---------|------|------|
| `src/NanoBot.Core/Storage/IFileStorageService.cs` | 修改 | 添加批量删除接口 |
| `src/NanoBot.Infrastructure/Storage/FileStorageService.cs` | 修改 | 实现批量删除 |
| `src/NanoBot.WebUI/Services/ISessionService.cs` | 修改 | 扩展接口方法 |
| `src/NanoBot.WebUI/Services/SessionService.cs` | 重构 | 实现 JSON 持久化 |
| `src/NanoBot.WebUI/Components/Layout/NavMenu.razor` | 修改 | 增强 UI 交互 |
| `src/NanoBot.WebUI/Components/Layout/NavMenu.razor.css` | 修改 | 添加样式 |
| `src/NanoBot.WebUI/Components/Dialogs/ConfirmDialog.razor` | 新增 | 删除确认对话框 |
| `src/NanoBot.WebUI/Components/Dialogs/RenameDialog.razor` | 新增 | 重命名对话框 |

## 技术选型说明

### 为什么选择 JSON 文件存储?

1. **简单性**: 无需额外的数据库依赖
2. **可靠性**: 数据直接可见，易于调试
3. **兼容性**: 与现有消息存储格式一致

### 未来升级方案

如需更强大的持久化能力，可以升级到：
- **SQLite**: 更轻量，适合移动端
- **UDataset + DuckDB**: 跨数据库支持

## 注意事项

1. **并发安全**: 使用 SemaphoreSlim 保证文件写入安全
2. **错误处理**: 数据库初始化失败时降级到 Agent 会话
3. **用户体验**: 删除操作需要确认防止误删
4. **性能**: 定时刷新间隔为 5 秒
