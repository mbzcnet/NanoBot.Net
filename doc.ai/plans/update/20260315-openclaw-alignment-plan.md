# NanoBot.Net OpenCrawl 对齐计划

**生成日期**: 2026-03-15

基于 Channels 实现差异对比报告，制定针对 openclaw (TypeScript) 的对齐计划。

---

## 概述

openclaw 是一个基于插件架构的 TypeScript 实现，其 Channel 系统具有高度模块化和丰富的适配器支持。相比 NanoBot.Net 当前实现，主要差距在于：

- 完整的插件系统架构
- 多账户 (account-scoped) 支持
- 高级通道适配器 (20+)
- 复杂的安全策略系统
- 心跳机制

---

## 差异分析

| 特性 | openclaw | NanoBot.Net | 差距 |
|------|----------|-------------|------|
| 插件架构 | ✅ ChannelPlugin<T> 泛型 | ❌ 手动 DI 注册 | 大 |
| 多账户支持 | ✅ account-scoped | ❌ | 大 |
| ChannelPlugin 适配器 | 20+ | ~5 | 中 |
| 安全策略 | 完整 (DM/群组/账号) | 基础 allow_from | 中 |
| 心跳机制 | ChannelHeartbeatAdapter | ❌ | 大 |
| 配置验证 | Zod schemas | 部分 | 中 |

---

## 详细任务列表

### 1. Channel 插件架构重构 [P0 - 高优先级]

**目标**: 引入类似 openclaw 的 ChannelPlugin 泛型接口，支持插件化架构和多账户。

**openclaw 参考**:
```typescript
// ChannelPlugin 泛型接口
export type ChannelPlugin<ResolvedAccount = any, Probe = unknown, Audit = unknown> = {
  id: ChannelId;
  meta: ChannelMeta;
  capabilities: ChannelCapabilities;
  
  // 配置相关
  config: ChannelConfigAdapter<ResolvedAccount>;
  configSchema?: ChannelConfigSchema;
  
  // 安全策略
  security?: ChannelSecurityAdapter<ResolvedAccount>;
  
  // 消息处理
  outbound?: ChannelOutboundAdapter;
  
  // 高级特性适配器
  groups?: ChannelGroupAdapter;
  mentions?: ChannelMentionAdapter;
  threading?: ChannelThreadingAdapter;
  streaming?: ChannelStreamingAdapter;
  messaging?: ChannelMessagingAdapter;
  directory?: ChannelDirectoryAdapter;
  resolver?: ChannelResolverAdapter;
  actions?: ChannelMessageActionAdapter;
  heartbeat?: ChannelHeartbeatAdapter;
};
```

**NanoBot.Net 现状**:
- `IChannel` 接口相对简单
- 通道通过 DI 手动注册
- 缺少泛型支持和适配器模式

**建议实现方案**:

#### 1.1 定义 ChannelPlugin 泛型接口

```csharp
// src/NanoBot.Core/Channels/ChannelPlugin.cs

/// <summary>
/// 通道插件元数据
/// </summary>
public record ChannelMeta(
    string Name,
    string Description,
    string Version,
    IReadOnlyList<string> SupportedMessageTypes);

/// <summary>
/// 通道能力定义
/// </summary>
public class ChannelCapabilities
{
    public bool SupportsDirectMessages { get; init; }
    public bool SupportsGroups { get; init; }
    public bool SupportsMedia { get; init; }
    public bool SupportsStreaming { get; init; }
    public bool SupportsThreading { get; init; }
    public bool SupportsMentions { get; init; }
    public bool SupportsReactions { get; init; }
    public bool SupportsMultiAccount { get; init; }
}

/// <summary>
/// 通道插件泛型接口 - 对标 openclaw ChannelPlugin<T>
/// </summary>
public interface IChannelPlugin<TAccount> where TAccount : class
{
    ChannelId Id { get; }
    ChannelMeta Meta { get; }
    ChannelCapabilities Capabilities { get; }
    
    // 配置适配器
    IChannelConfigAdapter<TAccount> Config { get; }
    IChannelConfigSchema? ConfigSchema { get; }
    
    // 安全适配器
    IChannelSecurityAdapter<TAccount>? Security { get; }
    
    // 出站消息适配器
    IChannelOutboundAdapter? Outbound { get; }
    
    // 高级特性适配器 (可选)
    IChannelGroupAdapter? Groups { get; }
    IChannelMentionAdapter? Mentions { get; }
    IChannelThreadingAdapter? Threading { get; }
    IChannelStreamingAdapter? Streaming { get; }
    IChannelMessagingAdapter? Messaging { get; }
    IChannelDirectoryAdapter? Directory { get; }
    IChannelResolverAdapter? Resolver { get; }
    IChannelMessageActionAdapter? Actions { get; }
    IChannelHeartbeatAdapter? Heartbeat { get; }
}
```

#### 1.2 定义适配器接口

```csharp
// src/NanoBot.Core/Channels/Adapters/

public interface IChannelConfigAdapter<TAccount>
{
    Task<TAccount> ResolveAccountAsync(AccountId accountId, CancellationToken ct = default);
    Task<IReadOnlyList<AccountId>> ListAccountsAsync(CancellationToken ct = default);
    Task<ChannelConfig> GetConfigAsync(AccountId accountId, CancellationToken ct = default);
}

public interface IChannelSecurityAdapter<TAccount>
{
    Task<bool> IsAllowedAsync(TAccount account, InboundMessage message, CancellationToken ct = default);
    Task<bool> IsGroupAdminAsync(TAccount account, string groupId, string userId, CancellationToken ct = default);
}

public interface IChannelOutboundAdapter
{
    Task SendAsync(OutboundMessage message, CancellationToken ct = default);
    Task SendMediaAsync(string chatId, MediaContent media, CancellationToken ct = default);
}

public interface IChannelHeartbeatAdapter
{
    Task<bool> CheckHealthAsync(CancellationToken ct = default);
    event EventHandler<ChannelHealthEventArgs>? HealthChanged;
}
```

#### 1.3 插件发现机制

```csharp
// src/NanoBot.Channels/PluginDiscovery/

public interface IChannelPluginDiscoverer
{
    Task<IReadOnlyList<DiscoveredPlugin>> DiscoverAsync(CancellationToken ct = default);
}

public class PluginAssemblyDiscoverer : IChannelPluginDiscoverer
{
    public async Task<IReadOnlyList<DiscoveredPlugin>> DiscoverAsync(CancellationToken ct = default)
    {
        var plugins = new List<DiscoveredPlugin>();
        
        // 扫描程序集中的 IChannelPlugin 实现
        var assemblies = await LoadPluginAssembliesAsync(ct);
        foreach (var asm in assemblies)
        {
            var types = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetInterfaces().Any(i => i.IsGenericType 
                    && i.GetGenericTypeDefinition() == typeof(IChannelPlugin<>)));
            
            foreach (var type in types)
            {
                plugins.Add(new DiscoveredPlugin(type));
            }
        }
        
        return plugins;
    }
}
```

**相关文件**:
- `src/NanoBot.Core/Channels/IChannel.cs` - 需重构
- `src/NanoBot.Channels/Abstractions/ChannelBase.cs`
- 新增: `src/NanoBot.Core/Channels/ChannelPlugin.cs`
- 新增: `src/NanoBot.Core/Channels/Adapters/` - 适配器接口
- 新增: `src/NanoBot.Channels/PluginDiscovery/` - 插件发现

---

### 2. 多账户支持 [P0 - 高优先级]

**目标**: 实现 account-scoped 配置，支持一个通道类型多个账号实例。

**openclaw 参考**:
- 每个通道支持配置多个账户
- 账户隔离，消息路由基于账户

**NanoBot.Net 现状**:
- 单一通道实例，无多账户概念

**建议实现**:

```csharp
// src/NanoBot.Core/Channels/Accounts/

public class ChannelAccount
{
    public string AccountId { get; init; } = "";
    public string AccountName { get; init; } = "";
    public ChannelConfig Config { get; init; } = null!;
    public AccountStatus Status { get; init; }
    public DateTime LastActive { get; init; }
}

public enum AccountStatus
{
    Active,
    Inactive,
    Error,
    Connecting
}

public interface IMultiAccountChannel
{
    Task<IReadOnlyList<ChannelAccount>> GetAccountsAsync(CancellationToken ct = default);
    Task<ChannelAccount?> GetAccountAsync(string accountId, CancellationToken ct = default);
    Task AddAccountAsync(ChannelAccount account, CancellationToken ct = default);
    Task RemoveAccountAsync(string accountId, CancellationToken ct = default);
    Task UpdateAccountStatusAsync(string accountId, AccountStatus status, CancellationToken ct = default);
}
```

**配置文件示例**:
```yaml
channels:
  telegram:
    accounts:
      - id: "main_bot"
        name: "主 Bot"
        bot_token: "xxx"
      - id: "secondary_bot"
        name: "备用 Bot"
        bot_token: "yyy"
```

**相关文件**:
- 新增: `src/NanoBot.Core/Channels/Accounts/`
- `src/NanoBot.Channels/ChannelManager.cs` - 需支持多账户路由
- 配置文件模型

---

### 3. 安全策略系统增强 [P1 - 中优先级]

**目标**: 实现类似 openclaw 的复杂安全策略，支持 DM 和群组不同策略。

**openclaw 参考**:
```typescript
security?: ChannelSecurityAdapter<ResolvedAccount>;
// 支持 DM 和群组不同的策略配置
```

**NanoBot.Net 现状**:
- 简单的 allow_from 列表检查

**建议实现**:

```csharp
// src/NanoBot.Core/Security/

public interface ISecurityPolicy
{
    Task<SecurityDecision> EvaluateAsync(
        SecurityContext context,
        CancellationToken ct = default);
}

public class SecurityContext
{
    public required IChannelAccount Account { get; init; }
    public required InboundMessage Message { get; init; }
    public MessageSource Source { get; init; }  // DirectMessage, Group, Channel
    public string? TargetUserId { get; init; }
    public string? TargetGroupId { get; init; }
}

public enum SecurityDecision
{
    Allow,
    Deny,
    AllowWithWarning
}

public class SecurityPolicyConfig
{
    // DM 策略
    public SecurityRule? DirectMessage { get; set; }
    
    // 群组策略
    public SecurityRule? Group { get; set; }
    
    // 频道策略
    public SecurityRule? Channel { get; set; }
    
    // 默认策略
    public SecurityRule Default { get; set; } = SecurityRule.AllowAll;
}

public enum SecurityRule
{
    AllowAll,
    DenyAll,
    AllowList,
    BlockList,
    AdminOnly,
    Custom
}
```

**相关文件**:
- 新增: `src/NanoBot.Core/Security/`
- `src/NanoBot.Channels/Abstractions/ChannelBase.cs` - 集成安全策略
- 各通道实现

---

### 4. 心跳机制 [P1 - 中优先级]

**目标**: 实现通道健康检查和心跳监控。

**openclaw 参考**:
```typescript
heartbeat?: ChannelHeartbeatAdapter;
```

**建议实现**:

```csharp
// src/NanoBot.Infrastructure/Health/

public interface IChannelHeartbeat
{
    string ChannelId { get; }
    string AccountId { get; }
    
    Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default);
    
    event EventHandler<HealthChangedEventArgs>? HealthChanged;
}

public class HealthStatus
{
    public HealthState State { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime LastChecked { get; init; }
    public TimeSpan? Latency { get; init; }
}

public enum HealthState
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}

public class ChannelHealthMonitor : BackgroundService
{
    private readonly IReadOnlyList<IChannelHeartbeat> _heartbeats;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var heartbeat in _heartbeats)
            {
                var status = await heartbeat.CheckHealthAsync(stoppingToken);
                // 记录或通知健康状态变化
            }
            
            await Task.Delay(_checkInterval, stoppingToken);
        }
    }
}
```

**相关文件**:
- 新增: `src/NanoBot.Infrastructure/Health/`
- 各通道实现 `IChannelHeartbeat`

---

### 5. 高级适配器实现 [P2 - 中优先级]

**目标**: 实现 openclaw 支持的各类适配器。

#### 5.1 群组管理适配器

```csharp
public interface IChannelGroupAdapter
{
    Task<IReadOnlyList<GroupInfo>> GetGroupsAsync(CancellationToken ct = default);
    Task<GroupInfo?> GetGroupAsync(string groupId, CancellationToken ct = default);
    Task InviteMemberAsync(string groupId, string userId, CancellationToken ct = default);
    Task RemoveMemberAsync(string groupId, string userId, CancellationToken ct = default);
    Task SetGroupAdminAsync(string groupId, string userId, bool isAdmin, CancellationToken ct = default);
}
```

#### 5.2 提及适配器

```csharp
public interface IChannelMentionAdapter
{
    Task<IReadOnlyList<UserInfo>> ParseMentionsAsync(string content, CancellationToken ct = default);
    Task<string> FormatMentionAsync(string userId, string displayName, CancellationToken ct = default);
}
```

#### 5.3 线程适配器

```csharp
public interface IChannelThreadingAdapter
{
    Task<string> CreateThreadAsync(string parentMessageId, string initialContent, CancellationToken ct = default);
    Task ReplyToThreadAsync(string threadId, string content, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetThreadMessagesAsync(string threadId, CancellationToken ct = default);
}
```

#### 5.4 流式适配器

```csharp
public interface IChannelStreamingAdapter
{
    IAsyncEnumerable<string> StreamMessageAsync(
        string chatId, 
        string content, 
        CancellationToken ct = default);
}
```

---

### 6. 配置验证增强 [P2 - 中优先级]

**目标**: 实现类似 openclaw 的 Zod 配置验证。

**openclaw 参考**:
- 每个通道定义 `configSchema`
- 启动时验证配置

**建议实现**:

```csharp
// 使用 FluentValidation 或 JSON Schema

public interface IChannelConfigValidator
{
    ValidationResult Validate<TConfig>(TConfig config) where TConfig : ChannelConfig;
}

public class TelegramChannelConfigValidator : AbstractValidator<TelegramChannelConfig>
{
    public TelegramChannelConfigValidator()
    {
        RuleFor(x => x.BotToken)
            .NotEmpty()
            .WithMessage("Bot token is required");
        
        RuleFor(x => x.AllowedChatIds)
            .Must(x => x == null || x.Count > 0 || x.Contains("*"))
            .WithMessage("Either specify allowed chats or use * for all");
    }
}
```

---

## 实施顺序建议

### 阶段一: 核心架构重构 (2-3周)

1. 定义 ChannelPlugin 泛型接口
2. 定义所有适配器接口
3. 实现插件发现机制 (Assembly scanning)
4. 重构 ChannelManager 支持插件

### 阶段二: 多账户支持 (1-2周)

1. 实现 ChannelAccount 模型
2. 修改通道配置结构支持多账户
3. 实现账户解析和路由
4. 更新配置文件结构

### 阶段三: 安全与健康 (1-2周)

1. 实现安全策略系统
2. 实现心跳机制
3. 集成到通道基类

### 阶段四: 高级适配器 (持续)

1. 实现群组管理适配器
2. 实现提及适配器
3. 实现线程适配器
4. 实现流式适配器

### 阶段五: 配置验证 (1周)

1. 为各通道添加配置验证器
2. 启动时配置校验

---

## 相关代码位置参考

| 模块 | 路径 |
|------|------|
| 通道核心 | `src/NanoBot.Core/Channels/` |
| 通道实现 | `src/NanoBot.Channels/Implementations/` |
| 通道抽象 | `src/NanoBot.Channels/Abstractions/` |
| 健康监控 | `src/NanoBot.Infrastructure/Health/` |
| 安全策略 | `src/NanoBot.Core/Security/` |

---

## 备注

- openclaw 的设计偏向 TypeScript 生态系统，很多特性需要用 C# 思维重新设计
- 插件发现可以参考 .NET 的 `AssemblyLoadContext` 和 MEF 框架
- 多账户支持需要考虑配置持久化和账户状态管理
- 建议保持向后兼容，逐步迁移现有 IChannel 实现

---

*计划创建时间: 2026-03-15*
