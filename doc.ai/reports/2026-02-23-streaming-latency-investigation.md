# 流式输出延迟问题调查报告

**日期**: 2026-02-23  
**问题**: NanoBot 流式输出首字延迟约 40 秒，而直接 curl 调用 Ollama 仅需 ~1-2 秒

## 问题描述

用户反馈启用流式输出后，AI 响应延迟高达 40 秒，虽然流式输出会让用户体验比非流式稍好（内容会逐步显示），但整体等待时间仍然过长。

## 调查过程

### 1. 确认问题存在
- curl 直接调用: ~1.6 秒
- NanoBot 流式: ~40 秒 (25倍差距)

### 2. 发现历史重复 bug (已修复)
日志显示首次请求发送了 63 条重复消息：
```
Message 0: role=user, content=Hello...
Message 1: role=user, content=Hello...
Message 2: role=user, content=Hello...
```

**根因**: `FileBackedChatHistoryProvider.InvokedCoreAsync` 每次请求都把 `context.RequestMessages` 写入文件，导致历史重复累积。

**修复**: 改为只写入响应消息。

### 3. 禁用历史加载后确认问题仍存在
清理历史后，消息数降为 1 条，但延迟仍然是 40 秒。

### 4. 框架验证测试
创建独立测试项目验证 Microsoft.Agents.AI 1.0.0-rc1：

| 测试场景 | 耗时 |
|---------|------|
| 基础框架流式 | 400-700ms |
| + UseFunctionInvocation | 400ms |
| + SanitizingChatClient | 400ms |
| + AIContextProvider | 700ms |
| + Session | 400-700ms |

**结论**: 框架本身流式很快，延迟问题不是框架 bug。

### 5. 完整配置对比测试
测试完全模拟 NanoBot 的配置（Client、中间件、Agent选项）：

| 配置 | 耗时 |
|------|------|
| 完整管道 (无Session) | 400ms |
| 完整管道 (有Session) | 130-400ms |

**结论**: 测试环境全部正常，问题在应用特定配置。

### 6. 可能的原因

1. **Session 加载问题**: 之前的 Session 文件中可能有大量历史消息（已清理）
2. **Ollama 模型冷启动**: 首次调用可能需要加载模型（但 curl 一直很快）
3. **网络/认证差异**: 测试使用 `local-no-key`，可能存在差异

## 已完成的修复

1. ✅ 流式输出功能实现
2. ✅ 历史重复累积 bug 修复
3. ✅ API 升级到 Microsoft.Agents.AI 1.0.0-rc1
4. ✅ 添加详细性能日志

## 待验证

请运行以下命令清理 Session 后重新测试：

```bash
rm -rf ~/.nbot/workspace/sessions/*
dotnet run -- agent --logs
```

如果问题仍然存在，需要进一步调试 Ollama 连接或添加更多日志。

## 附件

- 相关代码变更:
  - `src/NanoBot.Providers/ChatClientFactory.cs`
  - `src/NanoBot.Agent/AgentRuntime.cs`
  - `src/NanoBot.Agent/Context/FileBackedChatHistoryProvider.cs`
  - `src/NanoBot.Agent/NanoBotAgentFactory.cs`
