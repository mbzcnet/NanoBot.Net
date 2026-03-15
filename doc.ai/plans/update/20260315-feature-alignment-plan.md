# NanoBot.Net 功能对齐计划 (2026-03-15)

基于 NanoBot Python 项目 2026-03-08 ~ 2026-03-15 的更新，对比 NanoBot.Net 当前实现，制定功能对齐计划。

---

## 差异分析概览

| 功能 | Python 版本 | NanoBot.Net 状态 | 优先级 |
|------|-------------|------------------|--------|
| 交互式配置向导 | ✅ 697行 onboard_wizard.py | ⚠️ 已有基础 onboard 命令 | 中 |
| Channel 插件架构 | ✅ entry_points 插件发现 | ❌ 未实现 | 高 |
| 结构化评估 (Evaluator) | ✅ evaluator.py | ❌ 未实现 | 中 |
| MCP enabledTools | ✅ 支持指定工具过滤 | ❌ 未实现 | 中 |
| DingTalk 文件/图片/富文本 | ✅ 支持 | ⚠️ 需验证 | 低 |
| Feishu 工具调用展示 | ✅ 代码块展示 | ⚠️ 需验证 | 低 |
| 异常处理防止崩溃 | ✅ agent loop 保护 | ✅ 已实现 | - |

---

## 详细任务列表

### 1. Channel 插件架构 [高优先级]

**目标**: 实现基于 .NET 约定的 Channel 插件发现机制，类似 Python 的 entry_points。

**Python 参考实现**:
- 插件通过 `nanobot.channels` entry_point 组被发现
- Config 类从 `schema.py` 移至各自模块
- 每个 Channel 实现 `default_config()` 方法

**NanoBot.Net 现状**:
- Channel 通过 DI 手动注册
- Config 类集中在 `NanoBot.Core.Configuration`

**建议实现方案**:
```csharp
// 方案 A: 使用 Assembly 扫描 (推荐)
// 1. 创建 IChannel interface with DefaultConfig() method
// 2. 在启动时扫描程序集查找 IChannel 实现
// 3. 支持外部程序集加载

// 接口设计
public interface IChannel
{
    string Id { get; }
    string Type { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task SendMessageAsync(OutboundMessage message, CancellationToken ct = default);
    
    // 新增
    ChannelConfig? DefaultConfig();  // 返回默认配置用于 onboard
}
```

**相关文件**:
- `src/NanoBot.Channels/ChannelManager.cs`
- `src/NanoBot.Channels/Abstractions/ChannelBase.cs`
- `src/NanoBot.Core/Channels/IChannel.cs`

---

### 2. MCP enabledTools 支持 [中优先级]

**目标**: 在 McpServerConfig 中添加 enabledTools 配置，支持过滤 MCP 工具。

**Python 参考实现**:
```python
# 支持原始和包装后的工具名
# ["*"] 表示所有工具
# [] 表示无工具
# ["tool1", "tool2"] 表示指定工具
```

**NanoBot.Net 现状**:
```csharp
// src/NanoBot.Core/Configuration/Models/McpServerConfig.cs
public class McpServerConfig
{
    public string Command { get; set; } = string.Empty;
    public IReadOnlyList<string> Args { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Env { get; set; } = new();
    public string? Cwd { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public int ToolTimeout { get; set; } = 30;
    // ❌ 缺少 EnabledTools
}
```

**建议修改**:
```csharp
public class McpServerConfig
{
    // ... 现有字段
    
    /// <summary>
    /// 启用的工具列表。["*"] 表示所有工具，空列表表示无工具。
    /// 支持原始工具名和包装后的工具名 (mcp_server_toolname)。
    /// </summary>
    public List<string>? EnabledTools { get; set; }
}
```

**相关文件**:
- `src/NanoBot.Core/Configuration/Models/McpServerConfig.cs`
- `src/NanoBot.Tools/Mcp/McpClient.cs`
- `src/NanoBot.Tools/ToolProvider.cs`

---

### 3. 交互式配置向导增强 [中优先级]

**目标**: 增强现有的 onboard 命令，添加更丰富的交互式配置。

**Python 参考实现** (697行 onboard_wizard.py):
- 使用 questionary 库进行交互式问答
- 使用 Rich 库展示配置表格
- 自动发现并配置所有 Channel

**NanoBot.Net 现状**:
- 已有基本的 `OnboardCommand.cs` (~730行)
- 支持 LLM Profile、Workspace 配置
- 支持 Playwright 浏览器安装

**建议增强**:
1. 添加 Channel 配置向导
2. 添加 MCP 配置向导
3. 使用 ANSI 转义序列或库实现更友好的 UI

**相关文件**:
- `src/NanoBot.Cli/Commands/OnboardCommand.cs`

---

### 4. 结构化评估 (Evaluator) [中优先级]

**目标**: 实现后台任务结果的结构化评估，决定是否通知用户。

**Python 参考实现** (evaluator.py):
- 使用轻量级 LLM 调用决定是否通知
- 评估原始任务和 Agent 响应
- 返回 `should_notify` 和 `reason`

**NanoBot.Net 现状**:
- HeartbeatService 和 CronService 已实现
- 使用 `<SILENT_OK>` 标记或直接发送通知

**建议实现**:
```csharp
// 新增文件: src/NanoBot.Agent/ResponseEvaluator.cs
public interface IResponseEvaluator
{
    Task<bool> ShouldNotifyAsync(
        string response, 
        string taskContext, 
        CancellationToken ct = default);
}

// 实现使用轻量级 LLM 调用
```

**相关文件**:
- `src/NanoBot.Infrastructure/Heartbeat/HeartbeatService.cs`
- `src/NanoBot.Infrastructure/Cron/CronService.cs`

---

### 5. DingTalk 文件/图片/富文本支持 [低优先级]

**目标**: 验证并增强 DingTalkChannel 的文件接收能力。

**Python 参考实现**:
```python
# 支持 file/image/richText 消息类型
# 下载文件到 media 目录而非 /tmp
```

**NanoBot.Net 现状**:
- 已有基本的 DingTalkChannel 实现
- 需要验证是否支持文件/图片

**建议任务**:
1. 检查 DingTalkChannel 是否处理 file/image/richText
2. 确保文件下载到正确的 media 目录

**相关文件**:
- `src/NanoBot.Channels/Implementations/DingTalk/DingTalkChannel.cs`

---

### 6. Feishu 工具调用展示 [低优先级]

**目标**: 验证 Feishu 工具调用展示功能。

**Python 参考实现**:
- 工具调用以代码块格式展示
- 修复 think 标签 stripping 问题

**NanoBot.Net 现状**:
- FeishuChannel 已实现基本的卡片消息
- 需验证工具调用展示

**相关文件**:
- `src/NanoBot.Channels/Implementations/Feishu/FeishuChannel.cs`

---

## 实施顺序建议

1. **第一阶段** (1-2周): Channel 插件架构
2. **第二阶段** (1周): MCP enabledTools
3. **第三阶段** (1周): 交互式配置向导增强
4. **第四阶段** (1周): 结构化评估
5. **第五阶段**: 验证并修复 DingTalk/Feishu 功能

---

## 备注

- 异常处理已在 `AgentRuntime.cs` 中实现 (try-catch 保护 agent loop)
- NanoBot.Net 使用 Microsoft.Agents.AI 框架，与 Python 版本架构有所不同
- 部分功能需要考虑 .NET 生态的等价实现 (如 entry_points → Assembly scanning)

---

*计划创建时间: 2026-03-15*
