# NanoBot.WebUI 消息操作功能实现计划

## 概述
为 NanoBot.WebUI 添加消息级别的常见操作功能，包括重新编辑、重试、复制、删除等功能。

## 技术栈
- **框架**: Blazor Server + MudBlazor 9.0.0
- **语言**: C# / Razor
- **存储**: JSONL 文件 (SessionManager)

## 功能需求

### 1. 复制消息内容 (Copy)
- **触发方式**: 鼠标悬停消息气泡时显示操作栏，点击复制按钮
- **功能**: 将消息内容复制到剪贴板
- **适用角色**: 用户消息和 AI 消息都支持
- **实现方式**: 使用 `IJSRuntime` 调用浏览器剪贴板 API

### 2. 删除消息 (Delete)
- **触发方式**: 鼠标悬停消息气泡时显示操作栏，点击删除按钮
- **功能**: 删除选中的消息及其后续消息（因为消息有上下文依赖）
- **适用角色**: 用户消息和 AI 消息都支持
- **确认方式**: 显示确认对话框 (MudDialog)
- **实现方式**: 
  - 需要在 ISessionService 和 SessionService 中添加 DeleteMessageAsync 方法
  - 需要修改 SessionManager 支持删除特定消息

### 3. 重新编辑 (Edit)
- **触发方式**: 鼠标悬停用户消息气泡时显示操作栏，点击编辑按钮
- **功能**: 将用户消息内容加载到输入框，允许修改后重新发送
- **适用角色**: 仅用户消息
- **行为**: 
  - 点击编辑后，消息内容填充到输入框
  - 用户修改后发送，将删除原消息及后续所有消息，然后发送新消息

### 4. 重试/重新生成 (Retry/Regenerate)
- **触发方式**: 鼠标悬停 AI 消息气泡时显示操作栏，点击重试按钮
- **功能**: 重新生成 AI 的回复
- **适用角色**: 仅 AI 消息
- **行为**:
  - 删除当前 AI 消息及其后续消息
  - 重新发送上一条用户消息给 AI

## 实现步骤

### Phase 1: 后端服务扩展

#### 1.1 扩展 ISessionService 接口
文件: `/Users/victor/Code/NanoBot.Net/src/NanoBot.Core/Sessions/ISessionService.cs`

添加方法:
```csharp
Task DeleteMessageAsync(string sessionId, int messageIndex);
Task DeleteMessagesFromAsync(string sessionId, int fromIndex);
```

#### 1.2 实现 SessionService 方法
文件: `/Users/victor/Code/NanoBot.Net/src/NanoBot.WebUI/Services/SessionService.cs`

实现删除消息逻辑:
- 读取 session 文件
- 找到对应消息索引
- 删除该消息及后续消息
- 重新保存 session 文件

#### 1.3 扩展 ISessionManager 接口 (如需要)
文件: `/Users/victor/Code/NanoBot.Net/src/NanoBot.Agent/SessionManager.cs`

### Phase 2: 前端组件开发

#### 2.1 创建消息操作栏组件
文件: `/Users/victor/Code/NanoBot.Net/src/NanoBot.WebUI/Components/Parts/MessageActions.razor`

功能:
- 显示复制、编辑、删除、重试按钮
- 根据消息角色显示不同按钮
- 使用 MudBlazor 图标按钮

#### 2.2 修改 Chat.razor 页面
文件: `/Users/victor/Code/NanoBot.Net/src/NanoBot.WebUI/Components/Pages/Chat.razor`

修改内容:
- 在每个消息气泡上添加悬停效果
- 集成 MessageActions 组件
- 实现各操作的事件处理:
  - OnCopyMessage: 复制到剪贴板
  - OnDeleteMessage: 删除消息并刷新列表
  - OnEditMessage: 加载消息到输入框
  - OnRetryMessage: 重新生成回复

#### 2.3 添加样式
文件: `/Users/victor/Code/NanoBot.Net/src/NanoBot.WebUI/Components/Pages/Chat.razor.css`

添加:
- 消息操作栏样式
- 悬停效果
- 按钮样式

### Phase 3: JavaScript 互操作

#### 3.1 添加剪贴板功能
文件: `/Users/victor/Code/NanoBot.Net/src/NanoBot.WebUI/wwwroot/js/chat.js` (已存在，需要添加)

添加函数:
```javascript
function copyToClipboard(text) {
    return navigator.clipboard.writeText(text);
}
```

## 详细设计

### 消息操作栏设计

```
┌─────────────────────────────────────────┐
│ 消息内容                                  │
│ ...                                     │
├─────────────────────────────────────────┤
│ [复制] [编辑] [删除]    ← 用户消息        │
│ [复制] [重试] [删除]    ← AI 消息         │
└─────────────────────────────────────────┘
```

### 消息索引处理

由于 SessionService.GetMessagesAsync 返回的是合并后的消息列表，需要:
1. 在 ChatMessage 类中添加原始索引信息
2. 删除时根据索引映射到实际的 JSONL 文件行号

### 删除策略

删除消息时需要:
1. 删除选中的消息
2. 删除该消息之后的所有消息（保持上下文一致性）
3. 更新 session 文件
4. 刷新消息列表

## 文件修改清单

### 后端
1. `/Users/victor/Code/NanoBot.Net/src/NanoBot.Core/Sessions/ISessionService.cs` - 添加接口方法
2. `/Users/victor/Code/NanoBot.Net/src/NanoBot.WebUI/Services/SessionService.cs` - 实现删除逻辑
3. `/Users/victor/Code/NanoBot.Net/src/NanoBot.Agent/SessionManager.cs` - 可能需要添加底层支持

### 前端
1. `/Users/victor/Code/NanoBot.Net/src/NanoBot.WebUI/Components/Parts/MessageActions.razor` - 新建操作栏组件
2. `/Users/victor/Code/NanoBot.Net/src/NanoBot.WebUI/Components/Pages/Chat.razor` - 集成操作功能
3. `/Users/victor/Code/NanoBot.Net/src/NanoBot.WebUI/Components/Pages/Chat.razor.css` - 添加样式
4. `/Users/victor/Code/NanoBot.Net/src/NanoBot.WebUI/wwwroot/js/chat.js` - 添加剪贴板功能

## 风险与注意事项

1. **消息索引映射**: 由于消息在显示时会被合并（连续的 Assistant/Tool 消息合并），需要正确处理索引映射
2. **Session 文件格式**: 需要正确解析和修改 JSONL 格式，保留 metadata 行
3. **并发处理**: 考虑多个标签页同时操作的情况
4. **撤销功能**: 初期可以不实现，但保留扩展空间

## 后续扩展

1. 添加消息点赞/点踩功能
2. 添加消息分享功能
3. 添加消息引用/回复功能
4. 添加消息搜索功能
