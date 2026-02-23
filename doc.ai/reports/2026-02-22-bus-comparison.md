# Events 与 Queue 对比报告

## 执行日期
2026-02-22

---

## 一、概述

本报告对比 Python 原项目（nanobot）和 .NET 移植版本（NanoBot.Net）中消息总线（Events 和 Queue）的实现情况。

---

## 二、原项目（nanobot）实现

### 2.1 Events 定义

**文件位置**: `Temp/nanobot/nanobot/bus/events.py`

```python
@dataclass
class InboundMessage:
    """Message received from a chat channel."""
    channel: str  # telegram, discord, slack, whatsapp
    sender_id: str  # User identifier
    chat_id: str  # Chat/channel identifier
    content: str  # Message text
    timestamp: datetime = field(default_factory=datetime.now)
    media: list[str] = field(default_factory=list)  # Media URLs
    metadata: dict[str, Any] = field(default_factory=dict)

    @property
    def session_key(self) -> str:
        return f"{self.channel}:{self.chat_id}"

@dataclass
class OutboundMessage:
    """Message to send to a chat channel."""
    channel: str
    chat_id: str
    content: str
    reply_to: str | None = None
    media: list[str] = field(default_factory=list)
    metadata: dict[str, Any] = field(default_factory=dict)
```

### 2.2 Queue 实现

**文件位置**: `Temp/nanobot/nanobot/bus/queue.py`

```python
class MessageBus:
    """
    Async message bus that decouples chat channels from the agent core.
    """

    def __init__(self):
        self.inbound: asyncio.Queue[InboundMessage] = asyncio.Queue()
        self.outbound: asyncio.Queue[OutboundMessage] = asyncio.Queue()

    async def publish_inbound(self, msg: InboundMessage) -> None:
        await self.inbound.put(msg)

    async def consume_inbound(self) -> InboundMessage:
        return await self.inbound.get()

    async def publish_outbound(self, msg: OutboundMessage) -> None:
        await self.outbound.put(msg)

    async def consume_outbound(self) -> OutboundMessage:
        return await self.outbound.get()

    @property
    def inbound_size(self) -> int:
        return self.inbound.qsize()

    @property
    def outbound_size(self) -> int:
        return self.outbound.qsize()
```

**特点**:
- 使用 `asyncio.Queue` 实现异步队列
- 两个独立队列：inbound（通道→Agent）和 outbound（Agent→通道）
- 简单直接的消息发布/消费模式

---

## 三、移植版本（NanoBot.Net）实现

### 3.1 Events 定义

**InboundMessage** - `src/NanoBot.Core/Bus/InboundMessage.cs`:
```csharp
public record InboundMessage
{
    public required string Channel { get; init; }
    public required string SenderId { get; init; }
    public required string ChatId { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<string> Media { get; init; } = Array.Empty<string>();
    public IDictionary<string, object>? Metadata { get; init; }
    public string SessionKey => $"{Channel}:{ChatId}";
}
```

**OutboundMessage** - `src/NanoBot.Core/Bus/OutboundMessage.cs`:
```csharp
public record OutboundMessage
{
    public required string Channel { get; init; }
    public required string ChatId { get; init; }
    public required string Content { get; init; }
    public string? ReplyTo { get; init; }
    public IReadOnlyList<string> Media { get; init; } = Array.Empty<string>();
    public IDictionary<string, object>? Metadata { get; init; }
}
```

### 3.2 Queue 实现

**文件位置**: `src/NanoBot.Infrastructure/Bus/MessageBus.cs`

```csharp
public sealed class MessageBus : IMessageBus
{
    private readonly Channel<InboundMessage> _inboundChannel;
    private readonly Channel<OutboundMessage> _outboundChannel;
    private readonly Dictionary<string, Func<OutboundMessage, Task>> _outboundSubscribers;
    // ...
}
```

**核心方法**:
| 方法 | 功能 |
|------|------|
| `PublishInboundAsync` | 发布入站消息 |
| `ConsumeInboundAsync` | 消费入站消息 |
| `PublishOutboundAsync` | 发布出站消息 |
| `ConsumeOutboundAsync` | 消费出站消息 |
| `SubscribeOutbound` | 订阅特定通道的消息 |
| `StartDispatcherAsync` | 启动消息分发器 |
| `Stop` | 停止总线 |

**特点**:
- 使用 `System.Threading.Channels` 实现高效异步队列
- 支持消息订阅模式（SubscribeOutbound）
- 内置消息分发器（Dispatcher）
- 支持容量限制和背压
- 实现 `IDisposable` 资源管理

### 3.3 接口定义

**文件位置**: `src/NanoBot.Core/Bus/IMessageBus.cs`

```csharp
public interface IMessageBus : IDisposable
{
    ValueTask PublishInboundAsync(InboundMessage message, CancellationToken ct = default);
    ValueTask<InboundMessage> ConsumeInboundAsync(CancellationToken ct = default);
    ValueTask PublishOutboundAsync(OutboundMessage message, CancellationToken ct = default);
    ValueTask<OutboundMessage> ConsumeOutboundAsync(CancellationToken ct = default);

    void SubscribeOutbound(string channel, Func<OutboundMessage, Task> callback);
    Task StartDispatcherAsync(CancellationToken ct = default);
    void Stop();

    int InboundSize { get; }
    int OutboundSize { get; }
}
```

---

## 四、差异分析

### 4.1 消息类型对比

| 字段 | Python (nanobot) | C# (NanoBot.Net) |
|------|------------------|------------------|
| Channel | `str` | `string` ✅ |
| SenderId | `str` | `string` ✅ |
| ChatId | `str` | `string` ✅ |
| Content | `str` | `string` ✅ |
| Timestamp | `datetime` | `DateTimeOffset` ✅ |
| Media | `list[str]` | `IReadOnlyList<string>` ✅ |
| Metadata | `dict[str, Any]` | `IDictionary<string, object>` ✅ |
| ReplyTo | `str \| None` (仅 Outbound) | `string?` ✅ |
| SessionKey | `@property` | `init` property ✅ |

### 4.2 队列实现对比

| 方面 | Python (nanobot) | C# (NanoBot.Net) |
|------|------------------|------------------|
| 底层实现 | `asyncio.Queue` | `System.Threading.Channels` |
| 容量限制 | 无默认限制 | 可配置容量 |
| 消息订阅 | ❌ 不支持 | ✅ 支持 |
| 消息分发 | ❌ 不支持 | ✅ 内置 Dispatcher |
| 资源管理 | 手动 | `IDisposable` |
| 线程安全 | asyncio 管理 | Channels 内置 |

### 4.3 架构增强

.NET 版本相比 Python 版本有以下增强：

1. **消息订阅机制**：
   ```csharp
   // 允许通道注册回调处理出站消息
   void SubscribeOutbound(string channel, Func<OutboundMessage, Task> callback);
   ```

2. **内置分发器**：
   ```csharp
   // 自动将出站消息分发给对应通道
   Task StartDispatcherAsync(CancellationToken ct = default);
   ```

3. **容量控制**：
   ```csharp
   // 支持背压，防止内存溢出
   var options = new BoundedChannelOptions(capacity)
   {
       FullMode = BoundedChannelFullMode.Wait,
   };
   ```

4. **完整的生命周期管理**：
   ```csharp
   public void Stop();
   public void Dispose();
   ```

---

## 五、结论

### 5.1 实现状态

| 功能 | nanobot (Python) | NanoBot.Net (C#) | 状态 |
|------|------------------|------------------|------|
| InboundMessage 定义 | ✅ | ✅ | 正确移植 |
| OutboundMessage 定义 | ✅ | ✅ | 正确移植 |
| SessionKey 属性 | ✅ | ✅ | 正确移植 |
| 消息队列 | ✅ | ✅ | 正确移植 |
| 消息发布/消费 | ✅ | ✅ | 正确移植 |
| 队列大小查询 | ✅ | ✅ | 正确移植 |
| 消息订阅 | ❌ | ✅ | **增强实现** |
| 内置消息分发器 | ❌ | ✅ | **增强实现** |
| 容量限制/背压 | ❌ | ✅ | **增强实现** |
| 资源管理 | ❌ | ✅ | **增强实现** |

### 5.2 结论

**✅ 移植正确完成**

- Events（InboundMessage、OutboundMessage）完全对应
- Queue 基础功能完全对应
- .NET 版本在此基础上做了合理扩展

**✅ .NET 版本增强功能**：
- 消息订阅机制
- 内置消息分发器
- 容量控制和背压
- 完整的资源生命周期管理

这些增强使得 .NET 版本在生产环境中更加健壮。

---

## 附录：相关文件索引

### 原项目 (Python)
- `Temp/nanobot/nanobot/bus/events.py` - 消息类型定义
- `Temp/nanobot/nanobot/bus/queue.py` - MessageBus 实现

### 移植版本 (C#)
- `src/NanoBot.Core/Bus/InboundMessage.cs` - 入站消息
- `src/NanoBot.Core/Bus/OutboundMessage.cs` - 出站消息
- `src/NanoBot.Core/Bus/IMessageBus.cs` - 接口定义
- `src/NanoBot.Infrastructure/Bus/MessageBus.cs` - 实现
- `src/NanoBot.Agent/AgentRuntime.cs` - 使用示例
