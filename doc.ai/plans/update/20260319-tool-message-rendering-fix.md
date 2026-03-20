# Tool 消息 IN/OUT 渲染修复计划

**日期**: 2026-03-19
**问题**: 重新加载会话时，tool response 内容显示在 IN/OUT 边界之外

---

## 问题描述

当从 JSONL 文件重新加载聊天会话时，tool 消息的响应内容（JSON 字符串）错误地显示为普通消息文本，出现在 IN/OUT 可折叠区域之外。

**预期行为**:
- Tool call 参数应在 IN 区域显示
- Tool response 内容应在 OUT 区域显示
- 两者之间不应有额外内容

**实际行为**:
- Tool call 参数正确显示在 IN 区域
- Tool response 的 JSON 内容错误显示为消息正文（在 IN/OUT 之外）
- Tool response 内容又在 OUT 区域重复显示

---

## 问题分析

### 代码位置
`/Users/victor/Code/NanoBot.Net/src/NanoBot.WebUI/Services/SessionService.cs`

### 问题根源

在 `ConsolidateMessages` 方法（第 541-549 行）中，当遇到 role 为 "tool" 的消息且不与任何 assistant 消息关联时（`currentResponse` 为 null），代码会将该 tool 消息转换为 assistant 消息：

```csharp
if (msg.Role == "tool")
{
    msg.Role = "assistant";
}
```

这导致 tool 消息的 content（包含 JSON 字符串）被当作普通 assistant 消息内容显示。

### 数据流

1. `ReadMessagesFromJsonLines` 读取 JSONL 时，为 tool 消息创建 `tool_result` part，并在第 425-433 行清空 content
2. 但在某些情况下（如孤立的 tool 消息），content 可能未被正确清空
3. `ConsolidateMessages` 将 tool 消息转为 assistant 消息时，保留了 content
4. Chat.razor 渲染时，将 content 作为普通文本显示，同时通过 `tool_result` part 在 OUT 区域再次显示

---

## 修复方案

### 方案选择
**方案**: 在 `ConsolidateMessages` 中明确清空 tool 消息的 content

**原因**:
- Tool 消息的内容应该只在 `tool_result` part 中显示
- 不应该作为普通消息内容显示
- 这是最安全、最直接的修复方式

### 代码变更

**文件**: `src/NanoBot.WebUI/Services/SessionService.cs`
**位置**: 第 541-547 行

**修改前**:
```csharp
if (msg.Role == "tool")
{
    msg.Role = "assistant";
}
```

**修改后**:
```csharp
if (msg.Role == "tool")
{
    msg.Role = "assistant";
    // Tool 消息的内容应该只在 tool_result part 中显示，
    // 不应该作为普通消息内容显示
    msg.Content = string.Empty;
}
```

---

## 测试验证

### 手动测试步骤
1. 使用提供的 JSONL 文件 (`chat_76c16215392c41638ce89ff7c9519487.jsonl`)
2. 在 WebUI 中重新加载该会话
3. 验证：
   - IN 区域正确显示 tool call 参数
   - OUT 区域正确显示 tool response
   - 两者之间没有 JSON 字符串显示为普通文本

### 预期结果
- Tool response JSON 内容只出现在 OUT 区域
- 消息正文区域不显示 tool response 内容

---

## 相关文件

- `src/NanoBot.WebUI/Services/SessionService.cs` - 修复位置
- `src/NanoBot.WebUI/Components/Pages/Chat.razor` - 渲染逻辑
- `src/NanoBot.Core/Sessions/MessageInfo.cs` - 消息模型
- `tests/NanoBot.WebUI.Tests/SessionServiceReloadTests.cs` - 相关测试

---

## 后续建议

1. 考虑添加单元测试覆盖孤立的 tool 消息场景
2. 检查其他可能影响 content 显示的地方（如 `ReadMessagesFromJsonLines` 中的 content 处理逻辑）
3. 考虑在 `MessageInfo` 中添加验证逻辑，确保 tool 消息不会同时包含 content 和 tool_result parts
