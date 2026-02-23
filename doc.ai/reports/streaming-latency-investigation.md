# 流式输出延迟问题调查报告 (更新)

**日期**: 2026-02-23  
**问题**: NanoBot 流式输出首字延迟约 40-50 秒

## 根因分析

通过添加请求日志，发现问题在于 **Instructions 过大**：

```
Messages: 1, Total chars: 2, Instructions: 14,374 chars
```

### 组成分析

| 组件 | 字符数 | 说明 |
|------|--------|------|
| Bootstrap (AGENTS.md + SOUL.md + USER.md) | ~4,200 | 重复3次！ |
| Memory (MEMORY.md) | **~7,600** | **主要问题！** |
| Skills | ~1,200 | |
| Base identity | ~1,200 | |
| **总计** | **14,374** | |

### Memory 问题

`MEMORY.md` 包含完整的 **Recent Conversations** 历史记录，从最早的对话到最近的全部在里面。每次请求都发送完整历史，导致：
- 7,600+ 字符的冗余数据
- 每次请求都重复发送

### Bootstrap 重复问题

同样的内容（NanoBot 介绍）在 Instructions 中重复了 3 次！

## 解决方案

### 1. 限制 Memory 大小
修改 MemoryContextProvider，只保留最近 N 条或关键信息，不要发送完整历史。

### 2. 修复 Bootstrap 重复
检查 CompositeAIContextProvider，避免重复添加相同内容。

### 3. 临时方案
清理 MEMORY.md 或限制其大小。

## 建议

1. **短期**: 清理 MEMORY.md，删除旧的 Recent Conversations
2. **中期**: 修改 MemoryContextProvider，只提取关键信息而不是发送完整文件
3. **长期**: 实现更智能的记忆机制

## 请求日志位置

```
/var/folders/vt/qytcnl5d6hd_y37m41p4fncm0000gp/T/nanobot_requests/
```
