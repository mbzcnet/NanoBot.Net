# NanoBot.Net 消息格式增强计划

## 文档信息

- **计划日期**: 2026-03-10
- **执行日期**: 2026-03-10
- **来源**: 基于 [OpenCode 对比分析报告](../../reports/update/20250310-opencode-comparison.md)
- **参考项目**: OpenCode (https://github.com/opencode-ai/opencode)
- **优先级**: P1 (高优先级)
- **预计工期**: 3-4 周
- **目标版本**: v0.2.0
- **执行状态**: ✅ 已完成 Phase 1-3

---

## 1. 背景与目标

### 1.1 现状问题

当前 NanoBot.Net 的消息格式存在以下限制：

1. **内容单一**: 消息仅通过 `Content` 字符串字段传递，无法表达复杂结构
2. **工具状态不透明**: 工具调用状态无法实时跟踪和展示
3. **缺乏元数据**: 没有 token 使用、成本计算、模型信息等重要元数据
4. **显示静态**: 消息显示为静态文本，缺乏交互性和实时反馈

### 1.2 目标

参考 OpenCode 的设计，引入 **Part 系统**，实现：

1. **模块化消息结构**: 支持文本、工具调用、文件附件、推理过程等多种 Part 类型
2. **详细工具状态管理**: Pending → Running → Completed/Error 的完整生命周期
3. **丰富元数据支持**: Token 使用、成本计算、模型信息、时间戳等
4. **组件化显示**: 前端支持可折叠、分组、动画的丰富交互

---

## 2. 架构设计

### 2.1 Part 系统设计

```
┌─────────────────────────────────────────────────────────┐
│                    MessageWithParts                     │
├─────────────────────────────────────────────────────────┤
│  Id: string                                             │
│  SessionId: string                                      │
│  Role: "user" | "assistant"                             │
│  Timestamp: DateTimeOffset                              │
│  Metadata: MessageMetadata                              │
│  Parts: List<MessagePart>                               │
└─────────────────────────────────────────────────────────┘
                            │
        ┌───────────────────┼───────────────────┐
        │                   │                   │
        ▼                   ▼                   ▼
┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│   TextPart   │   │   ToolPart   │   │  Reasoning   │
└──────────────┘   └──────────────┘   └──────────────┘
        │                   │                   │
        ▼                   ▼                   ▼
┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│   FilePart   │   │  StepStart   │   │  StepFinish  │
└──────────────┘   └──────────────┘   └──────────────┘
```

### 2.2 与现有架构的集成

```
┌────────────────────────────────────────────────────────────┐
│                     AgentRuntime                           │
│  ┌──────────────────────────────────────────────────────┐  │
│  │                 ProcessMessageAsync                  │  │
│  │  1. 接收 InboundMessage                              │  │
│  │  2. 转换为 MessageWithParts                          │  │
│  │  3. 调用 _agent.RunAsync                              │  │
│  │  4. 流式返回工具调用状态和结果                        │  │
│  └──────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────────┐
│                 ChatClientAgent (MAA)                      │
│  - 使用 IChatClient 进行流式对话                          │
│  - 拦截 FunctionCallContents                              │
│  - 创建 ToolPart 并更新状态                               │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────────┐
│                   Tool Execution                           │
│  - Pending: 创建 ToolPart，添加到消息                     │
│  - Running: 更新状态，支持实时元数据                       │
│  - Completed/Error: 更新最终状态和输出                     │
└────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────────┐
│                 MessageBus / WebUI                         │
│  - 流式推送 Part 更新                                      │
│  - 前端组件化渲染                                          │
└────────────────────────────────────────────────────────────┘
```

---

## 3. 实施阶段

### Phase 1: 核心 Part 模型定义 (P0) - 第 1 周

#### 3.1.1 基础 Part 类型

**任务清单**:
- [ ] 创建 `MessagePart` 抽象基类
- [ ] 创建 `TextPart` 实现
- [ ] 创建 `ToolPart` 实现
- [ ] 创建 `ReasoningPart` 实现
- [ ] 创建 `FilePart` 实现

**技术细节**:

```csharp
// src/NanoBot.Core/Messages/MessagePart.cs
public abstract record MessagePart
{
    public required string Id { get; init; }
    public required string MessageId { get; init; }
    public required string SessionId { get; init; }
    public abstract string Type { get; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public record TextPart : MessagePart
{
    public override string Type => "text";
    public required string Text { get; init; }
    public bool Synthetic { get; init; }  // 是否由系统生成
    public bool Ignored { get; init; }    // 是否被忽略（不计入上下文）
}

public record ToolPart : MessagePart
{
    public override string Type => "tool";
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public required Dictionary<string, object> Input { get; init; }
    public required ToolState State { get; init; }
    public string? RawInput { get; init; }  // 原始 LLM 输出（用于调试）
}

public record ReasoningPart : MessagePart
{
    public override string Type => "reasoning";
    public required string Content { get; init; }
    public bool Summary { get; init; }  // 是否是总结性推理
}

public record FilePart : MessagePart
{
    public override string Type => "file";
    public required string FilePath { get; init; }
    public string? Content { get; init; }  // 文件内容（可选）
    public long? Size { get; init; }
    public string? MimeType { get; init; }
}
```

#### 3.1.2 工具状态管理

**任务清单**:
- [ ] 定义 `ToolState` 抽象状态类
- [ ] 实现 `PendingToolState`
- [ ] 实现 `RunningToolState`
- [ ] 实现 `CompletedToolState`
- [ ] 实现 `ErrorToolState`

**技术细节**:

```csharp
// src/NanoBot.Core/Messages/ToolStates.cs
public abstract record ToolState
{
    public abstract string Status { get; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public TimeSpan? Duration => CompletedAt - StartedAt;
}

public record PendingToolState : ToolState
{
    public override string Status => "pending";
}

public record RunningToolState : ToolState
{
    public override string Status => "running";
    public string? Title { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public record CompletedToolState : ToolState
{
    public override string Status => "completed";
    public required string Output { get; init; }
    public required string Title { get; init; }
    public List<FileAttachment> Attachments { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public record ErrorToolState : ToolState
{
    public override string Status => "error";
    public required string ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public string? StackTrace { get; init; }
}

public record FileAttachment
{
    public required string FilePath { get; init; }
    public string? Content { get; init; }
    public string? MimeType { get; init; }
}
```

#### 3.1.3 增强消息元数据

**任务清单**:
- [ ] 创建 `MessageMetadata` 记录
- [ ] 创建 `TokenUsage` 记录
- [ ] 创建 `ModelInfo` 记录
- [ ] 创建 `CostInfo` 记录

**技术细节**:

```csharp
// src/NanoBot.Core/Messages/MessageMetadata.cs
public record MessageMetadata
{
    public TokenUsage? Tokens { get; init; }
    public CostInfo? Cost { get; init; }
    public ModelInfo? Model { get; init; }
    public ErrorInfo? Error { get; init; }
    public Dictionary<string, object>? Custom { get; init; }
}

public record TokenUsage
{
    public int Input { get; init; }
    public int Output { get; init; }
    public int? Reasoning { get; init; }
    public int? Total => Input + Output + (Reasoning ?? 0);
    public CacheTokenUsage? Cache { get; init; }
}

public record CacheTokenUsage
{
    public int Read { get; init; }
    public int Write { get; init; }
}

public record CostInfo
{
    public decimal InputCost { get; init; }
    public decimal OutputCost { get; init; }
    public decimal? TotalCost { get; init; }
}

public record ModelInfo
{
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
}

public record ErrorInfo
{
    public required string Name { get; init; }
    public required string Message { get; init; }
    public string? Code { get; init; }
    public Dictionary<string, object>? Details { get; init; }
}
```

#### 3.1.4 消息容器

**任务清单**:
- [ ] 创建 `MessageWithParts` 记录
- [ ] 实现 Part 的 CRUD 操作
- [ ] 实现 Part 查询方法

**技术细节**:

```csharp
// src/NanoBot.Core/Messages/MessageWithParts.cs
public record MessageWithParts
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required string Role { get; init; }  // "user" | "assistant"
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
    public MessageMetadata? Metadata { get; init; }
    public List<MessagePart> Parts { get; init; } = new();
    public string? ParentId { get; init; }  // 用于 threading
    public string? Variant { get; init; }   // 用于多版本消息

    // 辅助方法
    public IEnumerable<ToolPart> GetToolParts() =>
        Parts.OfType<ToolPart>();

    public string? GetTextContent() =>
        Parts.OfType<TextPart>()
             .Select(p => p.Text)
             .FirstOrDefault();

    public IEnumerable<FilePart> GetFileParts() =>
        Parts.OfType<FilePart>();
}
```

**依赖关系**:
- 无外部依赖，纯模型定义

**验收标准**:
- [ ] 所有 Part 类型定义完成
- [ ] 单元测试覆盖基本属性访问
- [ ] 模型可以序列化/反序列化为 JSON

---

### Phase 2: AgentRuntime Part 集成 (P0) - 第 1-2 周

#### 3.2.1 消息转换适配器

**任务清单**:
- [ ] 创建 `MessageAdapter` 类用于新旧消息格式转换
- [ ] 实现 `InboundMessage` → `MessageWithParts` 转换
- [ ] 实现 `MessageWithParts` → `OutboundMessage` 转换

**技术细节**:

```csharp
// src/NanoBot.Agent/Messages/MessageAdapter.cs
public static class MessageAdapter
{
    public static MessageWithParts ToMessageWithParts(InboundMessage message)
    {
        var parts = new List<MessagePart>();

        // 文本内容转为 TextPart
        if (!string.IsNullOrEmpty(message.Content))
        {
            parts.Add(new TextPart
            {
                Id = Guid.NewGuid().ToString(),
                MessageId = message.Id ?? Guid.NewGuid().ToString(),
                SessionId = message.SessionKey,
                Text = message.Content
            });
        }

        // 媒体文件转为 FilePart
        foreach (var mediaPath in message.Media)
        {
            parts.Add(new FilePart
            {
                Id = Guid.NewGuid().ToString(),
                MessageId = parts.First().MessageId,
                SessionId = message.SessionKey,
                FilePath = mediaPath
            });
        }

        return new MessageWithParts
        {
            Id = message.Id ?? Guid.NewGuid().ToString(),
            SessionId = message.SessionKey,
            Role = "user",
            Parts = parts,
            Metadata = new MessageMetadata
            {
                Custom = message.Metadata?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value)
            }
        };
    }

    public static OutboundMessage ToOutboundMessage(MessageWithParts message)
    {
        var text = message.GetTextContent() ?? "";
        var files = message.GetFileParts().Select(f => f.FilePath).ToList();

        return new OutboundMessage
        {
            Channel = "",  // 从 session 获取
            ChatId = "",   // 从 session 获取
            Content = text,
            Media = files
        };
    }
}
```

#### 3.2.2 AgentRuntime Part 流式处理

**任务清单**:
- [ ] 修改 `AgentRuntime` 支持流式 Part 更新
- [ ] 实现工具调用状态变更回调
- [ ] 集成 `IProgress<ToolPartUpdate>` 接口

**技术细节**:

```csharp
// src/NanoBot.Agent/AgentRuntime.cs (修改)
public class AgentRuntime : IAgentRuntime
{
    private readonly ConcurrentDictionary<string, MessageWithParts> _activeMessages = new();
    private readonly IMessageBus _messageBus;

    // 流式处理消息
    public async Task ProcessMessageWithPartsAsync(
        InboundMessage inboundMessage,
        IProgress<MessagePartUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var message = MessageAdapter.ToMessageWithParts(inboundMessage);
        _activeMessages[message.Id] = message;

        // 通知前端消息开始
        progress?.Report(new MessagePartUpdate
        {
            MessageId = message.Id,
            Type = UpdateType.MessageStarted,
            Data = message
        });

        try
        {
            // 使用 MAA 的 ChatClientAgent 进行流式对话
            var responseStream = _agent.RunAsync(
                message.Parts,
                new AgentRuntimeOptions
                {
                    OnToolExecuting = (toolPart) =>
                    {
                        // 更新为 Running 状态
                        UpdateToolState(message.Id, toolPart.CallId,
                            new RunningToolState { StartedAt = DateTimeOffset.UtcNow });

                        progress?.Report(new MessagePartUpdate
                        {
                            MessageId = message.Id,
                            PartId = toolPart.Id,
                            Type = UpdateType.ToolStateChanged,
                            Data = toolPart
                        });
                    },
                    OnToolExecuted = (toolPart, result) =>
                    {
                        // 更新为 Completed 状态
                        UpdateToolState(message.Id, toolPart.CallId,
                            new CompletedToolState
                            {
                                Output = result.Output,
                                Title = result.Title,
                                CompletedAt = DateTimeOffset.UtcNow
                            });

                        progress?.Report(new MessagePartUpdate
                        {
                            MessageId = message.Id,
                            PartId = toolPart.Id,
                            Type = UpdateType.ToolStateChanged,
                            Data = toolPart
                        });
                    }
                },
                cancellationToken);

            // 流式接收响应并添加 Part
            await foreach (var part in responseStream)
            {
                AddPartToMessage(message.Id, part);
                progress?.Report(new MessagePartUpdate
                {
                    MessageId = message.Id,
                    PartId = part.Id,
                    Type = UpdateType.PartAdded,
                    Data = part
                });
            }
        }
        finally
        {
            _activeMessages.TryRemove(message.Id, out _);
        }
    }

    private void UpdateToolState(string messageId, string callId, ToolState newState)
    {
        if (_activeMessages.TryGetValue(messageId, out var message))
        {
            var toolPart = message.Parts
                .OfType<ToolPart>()
                .FirstOrDefault(p => p.CallId == callId);

            if (toolPart != null)
            {
                var index = message.Parts.IndexOf(toolPart);
                message.Parts[index] = toolPart with { State = newState };
            }
        }
    }
}

// 更新事件
public record MessagePartUpdate
{
    public required string MessageId { get; init; }
    public string? PartId { get; init; }
    public required UpdateType Type { get; init; }
    public required object Data { get; init; }
}

public enum UpdateType
{
    MessageStarted,
    PartAdded,
    PartUpdated,
    ToolStateChanged,
    MessageCompleted,
    MessageError
}
```

#### 3.2.3 工具调用拦截与 Part 创建

**任务清单**:
- [ ] 创建 `PartAwareToolDecorator` 包装工具调用
- [ ] 拦截 `AIFunction` 调用并创建 ToolPart
- [ ] 支持工具执行过程中的元数据更新

**技术细节**:

```csharp
// src/NanoBot.Agent/Tools/PartAwareToolDecorator.cs
public class PartAwareToolDecorator : AITool
{
    private readonly AITool _innerTool;
    private readonly string _sessionId;
    private readonly string _messageId;
    private readonly IProgress<MessagePartUpdate> _progress;

    public PartAwareToolDecorator(
        AITool innerTool,
        string sessionId,
        string messageId,
        IProgress<MessagePartUpdate> progress)
    {
        _innerTool = innerTool;
        _sessionId = sessionId;
        _messageId = messageId;
        _progress = progress;
    }

    public override string Name => _innerTool.Name;
    public override string Description => _innerTool.Description;
    public override JsonElement JsonSchema => _innerTool.JsonSchema;

    public override async Task<FunctionResult> InvokeAsync(
        IEnumerable<KeyValuePair<string, object?>> arguments,
        CancellationToken cancellationToken = default)
    {
        var callId = Guid.NewGuid().ToString();

        // 创建 Pending 状态的 ToolPart
        var toolPart = new ToolPart
        {
            Id = Guid.NewGuid().ToString(),
            MessageId = _messageId,
            SessionId = _sessionId,
            CallId = callId,
            ToolName = Name,
            Input = arguments.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value ?? ""),
            State = new PendingToolState()
        };

        // 通知前端工具调用开始
        _progress.Report(new MessagePartUpdate
        {
            MessageId = _messageId,
            PartId = toolPart.Id,
            Type = UpdateType.PartAdded,
            Data = toolPart
        });

        try
        {
            // 更新为 Running 状态
            toolPart = toolPart with
            {
                State = new RunningToolState
                {
                    StartedAt = DateTimeOffset.UtcNow,
                    Title = $"Executing {Name}..."
                }
            };

            _progress.Report(new MessagePartUpdate
            {
                MessageId = _messageId,
                PartId = toolPart.Id,
                Type = UpdateType.ToolStateChanged,
                Data = toolPart
            });

            // 执行实际工具调用
            var result = await _innerTool.InvokeAsync(arguments, cancellationToken);

            // 更新为 Completed 状态
            toolPart = toolPart with
            {
                State = new CompletedToolState
                {
                    Output = result.ToString() ?? "",
                    Title = $"{Name} completed",
                    CompletedAt = DateTimeOffset.UtcNow
                }
            };

            _progress.Report(new MessagePartUpdate
            {
                MessageId = _messageId,
                PartId = toolPart.Id,
                Type = UpdateType.ToolStateChanged,
                Data = toolPart
            });

            return result;
        }
        catch (Exception ex)
        {
            // 更新为 Error 状态
            toolPart = toolPart with
            {
                State = new ErrorToolState
                {
                    ErrorMessage = ex.Message,
                    CompletedAt = DateTimeOffset.UtcNow
                }
            };

            _progress.Report(new MessagePartUpdate
            {
                MessageId = _messageId,
                PartId = toolPart.Id,
                Type = UpdateType.ToolStateChanged,
                Data = toolPart
            });

            throw;
        }
    }
}
```

**依赖关系**:
- 依赖 Phase 1 的 Part 模型
- 依赖现有的 AgentRuntime 和 ChatClientAgent

**验收标准**:
- [ ] AgentRuntime 可以流式返回 Part 更新
- [ ] 工具调用状态变化可以实时通知
- [ ] 向后兼容现有 InboundMessage/OutboundMessage 接口

---

### Phase 3: WebUI Part 显示组件 (P0) - 第 2-3 周

#### 3.3.1 消息组件框架

**任务清单**:
- [ ] 创建 `MessagePartComponent` 基类
- [ ] 实现 `TextPartComponent`
- [ ] 实现 `ToolPartComponent`
- [ ] 实现 `FilePartComponent`

**技术细节** (Blazor 组件):

```razor
@* src/NanoBot.WebUI/Components/MessagePartComponent.razor *@
@code {
    [Parameter]
    public MessagePart Part { get; set; } = default!;

    [Parameter]
    public bool HideDetails { get; set; } = false;

    [Parameter]
    public bool DefaultOpen { get; set; } = false;
}

@switch (Part)
{
    case TextPart textPart:
        <TextPartComponent Part="textPart" />
        break;
    case ToolPart toolPart:
        <ToolPartComponent Part="toolPart"
                          HideDetails="HideDetails"
                          DefaultOpen="DefaultOpen" />
        break;
    case FilePart filePart:
        <FilePartComponent Part="filePart" />
        break;
    case ReasoningPart reasoningPart:
        <ReasoningPartComponent Part="reasoningPart" />
        break;
    default:
        <div class="unknown-part">Unknown part type: @Part.Type</div>
        break;
}
```

```razor
@* src/NanoBot.WebUI/Components/ToolPartComponent.razor *@
@implements IDisposable

<div class="tool-part @GetStatusClass()">
    <div class="tool-header" @onclick="ToggleOpen">
        <div class="tool-icon">
            <ToolIcon ToolName="Part.ToolName" />
        </div>
        <div class="tool-title">
            @GetDisplayTitle()
        </div>
        <div class="tool-status">
            <ToolStatusIndicator State="Part.State" />
        </div>
        <div class="tool-toggle">
            @if (IsOpen)
            {
                <i class="bi bi-chevron-up"></i>
            }
            else
            {
                <i class="bi bi-chevron-down"></i>
            }
        </div>
    </div>

    @if (IsOpen)
    {
        <div class="tool-content">
            @switch (Part.State)
            {
                case PendingToolState:
                    <div class="tool-pending">
                        <span class="spinner-border spinner-border-sm"></span>
                        Waiting to execute...
                    </div>
                    break;

                case RunningToolState running:
                    <div class="tool-running">
                        <span class="spinner-border spinner-border-sm text-primary"></span>
                        @running.Title
                        @if (running.Metadata.Any())
                        {
                            <div class="tool-metadata">
                                @foreach (var meta in running.Metadata)
                                {
                                    <span class="badge bg-info">@meta.Key: @meta.Value</span>
                                }
                            </div>
                        }
                    </div>
                    break;

                case CompletedToolState completed:
                    <div class="tool-completed">
                        <div class="tool-output">
                            <pre><code>@completed.Output</code></pre>
                        </div>
                        @if (completed.Attachments.Any())
                        {
                            <div class="tool-attachments">
                                @foreach (var attachment in completed.Attachments)
                                {
                                    <FileAttachmentComponent Attachment="attachment" />
                                }
                            </div>
                        }
                    </div>
                    break;

                case ErrorToolState error:
                    <div class="tool-error alert alert-danger">
                        <i class="bi bi-exclamation-triangle"></i>
                        @error.ErrorMessage
                    </div>
                    break;
            }

            @if (!HideDetails)
            {
                <div class="tool-details">
                    <details>
                        <summary>Input Parameters</summary>
                        <pre><code>@JsonSerializer.Serialize(Part.Input, new JsonSerializerOptions { WriteIndented = true })</code></pre>
                    </details>
                </div>
            }
        </div>
    }
</div>

@code {
    [Parameter] public ToolPart Part { get; set; } = default!;
    [Parameter] public bool HideDetails { get; set; }
    [Parameter] public bool DefaultOpen { get; set; }

    private bool IsOpen { get; set; }

    protected override void OnInitialized()
    {
        IsOpen = DefaultOpen;
    }

    private void ToggleOpen() => IsOpen = !IsOpen;

    private string GetStatusClass() => Part.State.Status switch
    {
        "pending" => "tool-pending-border",
        "running" => "tool-running-border",
        "completed" => "tool-completed-border",
        "error" => "tool-error-border",
        _ => ""
    };

    private string GetDisplayTitle()
    {
        var title = Part.State switch
        {
            RunningToolState r => r.Title,
            CompletedToolState c => c.Title,
            _ => $"{Part.ToolName}"
        };

        var subtitle = GetToolSubtitle();
        if (!string.IsNullOrEmpty(subtitle))
        {
            return $"{title}: {subtitle}";
        }
        return title;
    }

    private string? GetToolSubtitle()
    {
        // 从 Input 中提取有意义的副标题
        return Part.Input.TryGetValue("path", out var path)
            ? Path.GetFileName(path?.ToString())
            : Part.Input.TryGetValue("query", out var query)
                ? query?.ToString()?.Truncate(30)
                : null;
    }

    public void Dispose() { }
}
```

#### 3.3.2 工具图标映射

**任务清单**:
- [ ] 创建工具名称到图标的映射表
- [ ] 实现 `ToolIcon` 组件
- [ ] 支持自定义图标扩展

**技术细节**:

```csharp
// src/NanoBot.WebUI/Services/ToolIconService.cs
public class ToolIconService
{
    private static readonly Dictionary<string, ToolInfo> _toolInfoMap = new()
    {
        ["read_file"] = new("glasses", "Read File"),
        ["list_directory"] = new("list-ul", "List Directory"),
        ["glob"] = new("search", "Glob Search"),
        ["grep"] = new("text-search", "Grep Search"),
        ["write_file"] = new("pencil", "Write File"),
        ["edit_file"] = new("edit", "Edit File"),
        ["bash"] = new("terminal", "Execute Command"),
        ["web_search"] = new("globe", "Web Search"),
        ["web_fetch"] = new("download", "Fetch Web Page"),
        ["spawn"] = new("robot", "Spawn Subagent"),
    };

    public ToolInfo GetToolInfo(string toolName)
    {
        return _toolInfoMap.GetValueOrDefault(toolName,
            new ToolInfo("gear", toolName.Humanize()));
    }
}

public record ToolInfo(string IconClass, string DisplayName);
```

#### 3.3.3 上下文工具分组

**任务清单**:
- [ ] 实现 `ContextToolGroup` 组件
- [ ] 按工具类型智能分组
- [ ] 实现分组状态指示器

**技术细节**:

```razor
@* src/NanoBot.WebUI/Components/ContextToolGroup.razor *@
<div class="context-tool-group">
    <div class="context-tool-header" @onclick="ToggleOpen">
        <div class="context-tool-icon">
            <i class="bi bi-folder"></i>
        </div>
        <div class="context-tool-title">
            @if (IsAnyToolRunning)
            {
                <span>Gathering Context</span>
                <span class="spinner-border spinner-border-sm ms-2"></span>
            }
            else
            {
                <span>Context Gathered</span>
            }
        </div>
        <div class="context-tool-summary">
            <AnimatedCountList Items="GetSummaryCounts()" />
        </div>
        <div class="context-tool-toggle">
            <i class="bi @(IsOpen ? "chevron-up" : "chevron-down")"></i>
        </div>
    </div>

    @if (IsOpen)
    {
        <div class="context-tool-list">
            @foreach (var toolPart in ToolParts)
            {
                <ToolPartComponent Part="toolPart"
                                  DefaultOpen="false"
                                  HideDetails="true" />
            }
        </div>
    }
</div>

@code {
    [Parameter] public List<ToolPart> ToolParts { get; set; } = new();

    private bool IsOpen { get; set; } = false;

    private bool IsAnyToolRunning => ToolParts.Any(p => p.State is RunningToolState);

    private void ToggleOpen() => IsOpen = !IsOpen;

    private List<CountItem> GetSummaryCounts()
    {
        var readCount = ToolParts.Count(p => p.ToolName == "read_file");
        var searchCount = ToolParts.Count(p =>
            p.ToolName is "glob" or "grep");
        var listCount = ToolParts.Count(p => p.ToolName == "list_directory");

        return new List<CountItem>
        {
            new("read", readCount, "glasses", "Read"),
            new("search", searchCount, "search", "Search"),
            new("list", listCount, "list-ul", "List")
        }.Where(i => i.Count > 0).ToList();
    }
}

public record CountItem(string Key, int Count, string Icon, string Label);
```

#### 3.3.4 实时状态同步

**任务清单**:
- [ ] 实现 SignalR Hub 用于 Part 更新推送
- [ ] 前端订阅消息流
- [ ] 实现乐观更新和状态同步

**技术细节**:

```csharp
// src/NanoBot.WebUI/Hubs/MessagePartHub.cs
public class MessagePartHub : Hub
{
    public async Task SubscribeToMessage(string messageId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"msg:{messageId}");
    }

    public async Task UnsubscribeFromMessage(string messageId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"msg:{messageId}");
    }
}

// src/NanoBot.WebUI/Services/MessagePartStreamer.cs
public class MessagePartStreamer : IProgress<MessagePartUpdate>
{
    private readonly IHubContext<MessagePartHub> _hubContext;

    public void Report(MessagePartUpdate update)
    {
        _hubContext.Clients
            .Group($"msg:{update.MessageId}")
            .SendAsync("PartUpdated", update);
    }
}
```

**JavaScript 前端** (简化):

```typescript
// WebUI 前端 TypeScript
class MessagePartStream {
    private connection: signalR.HubConnection;

    async connect() {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/messageParts")
            .build();

        this.connection.on("PartUpdated", (update: MessagePartUpdate) => {
            this.handleUpdate(update);
        });

        await this.connection.start();
    }

    async subscribeToMessage(messageId: string) {
        await this.connection.invoke("SubscribeToMessage", messageId);
    }

    private handleUpdate(update: MessagePartUpdate) {
        // 更新本地状态，触发 UI 重渲染
        messageStore.updatePart(update.messageId, update.partId, update.data);
    }
}
```

**依赖关系**:
- 依赖 Phase 1 和 Phase 2
- 依赖 SignalR 库

**验收标准**:
- [ ] WebUI 可以显示 Part 组件
- [ ] 工具状态变化可以实时反映在 UI 上
- [ ] 支持上下文工具分组显示

---

### Phase 4: 元数据与成本跟踪 (P1) - 第 3-4 周

#### 3.4.1 Token 使用跟踪

**任务清单**:
- [ ] 在 ChatClientAgent 中集成 Token 计数
- [ ] 实现 IChatClient 装饰器用于 Token 统计
- [ ] 将 Token 使用数据附加到 MessageMetadata

**技术细节**:

```csharp
// src/NanoBot.Providers/Decorators/TokenCountingChatClient.cs
public class TokenCountingChatClient : IChatClient
{
    private readonly IChatClient _innerClient;
    private readonly ILogger<TokenCountingChatClient> _logger;

    public TokenCountingChatClient(
        IChatClient innerClient,
        ILogger<TokenCountingChatClient> logger)
    {
        _innerClient = innerClient;
        _logger = logger;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _innerClient.GetResponseAsync(
            messages, options, cancellationToken);

        // 提取 Token 使用信息
        var usage = response.Usage;
        if (usage != null)
        {
            _logger.LogInformation(
                "Token usage - Input: {Input}, Output: {Output}, Total: {Total}",
                usage.InputTokenCount,
                usage.OutputTokenCount,
                usage.TotalTokenCount);

            // 存储到上下文
            TokenUsageContext.Current = new TokenUsage
            {
                Input = usage.InputTokenCount,
                Output = usage.OutputTokenCount ?? 0,
                Total = usage.TotalTokenCount
            };
        }

        return response;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // 流式响应处理类似
        return _innerClient.GetStreamingResponseAsync(
            messages, options, cancellationToken);
    }
}

// 异步本地上下文
public static class TokenUsageContext
{
    private static readonly AsyncLocal<TokenUsage?> _current = new();

    public static TokenUsage? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
```

#### 3.4.2 成本计算服务

**任务清单**:
- [ ] 创建 `ICostCalculator` 接口
- [ ] 实现基于模型定价的成本计算
- [ ] 支持自定义定价配置

**技术细节**:

```csharp
// src/NanoBot.Core/Services/ICostCalculator.cs
public interface ICostCalculator
{
    CostInfo CalculateCost(TokenUsage usage, ModelInfo model);
}

// src/NanoBot.Infrastructure/Services/CostCalculator.cs
public class CostCalculator : ICostCalculator
{
    // 模型定价配置（每 1K tokens 的价格）
    private static readonly Dictionary<string, ModelPricing> _pricing = new()
    {
        ["gpt-4"] = new(0.03m, 0.06m),
        ["gpt-4-turbo"] = new(0.01m, 0.03m),
        ["claude-3-opus"] = new(0.015m, 0.075m),
        ["claude-3-sonnet"] = new(0.003m, 0.015m),
        // ... 更多模型
    };

    public CostInfo CalculateCost(TokenUsage usage, ModelInfo model)
    {
        var pricing = _pricing.GetValueOrDefault(model.ModelId, new ModelPricing(0, 0));

        var inputCost = (usage.Input / 1000m) * pricing.InputPrice;
        var outputCost = (usage.Output / 1000m) * pricing.OutputPrice;

        return new CostInfo
        {
            InputCost = inputCost,
            OutputCost = outputCost,
            TotalCost = inputCost + outputCost
        };
    }
}

public record ModelPricing(decimal InputPrice, decimal OutputPrice);
```

#### 3.4.3 会话级成本统计

**任务清单**:
- [ ] 扩展 Session 存储成本信息
- [ ] 实现会话成本汇总 API
- [ ] 前端显示成本统计面板

**技术细节**:

```csharp
// src/NanoBot.Core/Models/SessionCostSummary.cs
public record SessionCostSummary
{
    public required string SessionId { get; init; }
    public int TotalMessages { get; init; }
    public TokenUsage TotalTokens { get; init; }
    public CostInfo TotalCost { get; init; }
    public List<ModelUsageByModel> ByModel { get; init; } = new();
}

public record ModelUsageByModel
{
    public required string ModelId { get; init; }
    public int MessageCount { get; init; }
    public TokenUsage Tokens { get; init; }
    public CostInfo Cost { get; init; }
}
```

**依赖关系**:
- 依赖 Phase 1-3

**验收标准**:
- [ ] 可以跟踪每个消息的 Token 使用
- [ ] 可以计算每个消息的成本
- [ ] 会话级成本统计可用

---

### Phase 5: 持久化与历史查询 (P1) - 第 4 周

#### 3.5.1 Part 序列化

**任务清单**:
- [ ] 实现 Part 的 JSON 序列化
- [ ] 处理多态类型序列化
- [ ] 确保向后兼容

**技术细节**:

```csharp
// src/NanoBot.Infrastructure/Serialization/MessagePartJsonConverter.cs
public class MessagePartJsonConverter : JsonConverter<MessagePart>
{
    public override MessagePart Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var type = root.GetProperty("type").GetString();
        var json = root.GetRawText();

        return type switch
        {
            "text" => JsonSerializer.Deserialize<TextPart>(json, options)!,
            "tool" => JsonSerializer.Deserialize<ToolPart>(json, options)!,
            "reasoning" => JsonSerializer.Deserialize<ReasoningPart>(json, options)!,
            "file" => JsonSerializer.Deserialize<FilePart>(json, options)!,
            _ => throw new NotSupportedException($"Unknown part type: {type}")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        MessagePart value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
```

#### 3.5.2 历史存储格式升级

**任务清单**:
- [ ] 升级 `HISTORY.md` 存储格式
- [ ] 支持新旧格式并存
- [ ] 实现历史记录 Part 查询

**技术细节**:

```csharp
// src/NanoBot.Infrastructure/Memory/PartAwareHistoryStore.cs
public class PartAwareHistoryStore : IHistoryStore
{
    private readonly string _historyPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public async Task AppendMessageAsync(MessageWithParts message)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        await File.AppendAllTextAsync(_historyPath, $"---\n{json}\n");
    }

    public async Task<IEnumerable<MessageWithParts>> GetHistoryAsync(
        string sessionId,
        int limit = 50)
    {
        // 解析历史文件，支持新旧格式
        var content = await File.ReadAllTextAsync(_historyPath);
        var entries = content.Split("---\n", StringSplitOptions.RemoveEmptyEntries);

        return entries
            .Select(ParseEntry)
            .Where(m => m.SessionId == sessionId)
            .TakeLast(limit);
    }

    private MessageWithParts ParseEntry(string entry)
    {
        // 尝试新格式
        try
        {
            return JsonSerializer.Deserialize<MessageWithParts>(entry, _jsonOptions)!;
        }
        catch
        {
            // 回退到旧格式（纯文本）
            return new MessageWithParts
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = "",
                Role = "assistant",
                Parts = new List<MessagePart>
                {
                    new TextPart { Text = entry }
                }
            };
        }
    }
}
```

**依赖关系**:
- 依赖 Phase 1-4

**验收标准**:
- [ ] Part 可以正确序列化和反序列化
- [ ] 历史文件可以按新格式存储
- [ ] 向后兼容旧格式历史记录

---

## 4. 依赖关系图

```
Phase 1: 核心 Part 模型定义 ✅
    │
    ├───> Phase 2: AgentRuntime Part 集成
    │           │
    │           ├───> Phase 3: WebUI Part 显示组件
    │           │           │
    │           │           └───> Phase 5: 持久化与历史查询
    │           │
    │           └───> Phase 4: 元数据与成本跟踪
    │
    └───> Phase 4: 元数据与成本跟踪 (独立)
```

---

## 5. 修改文件清单

### 核心模型
- `src/NanoBot.Core/Messages/MessagePart.cs` - Part 抽象基类
- `src/NanoBot.Core/Messages/TextPart.cs` - 文本 Part
- `src/NanoBot.Core/Messages/ToolPart.cs` - 工具 Part
- `src/NanoBot.Core/Messages/ReasoningPart.cs` - 推理 Part
- `src/NanoBot.Core/Messages/FilePart.cs` - 文件 Part
- `src/NanoBot.Core/Messages/ToolStates.cs` - 工具状态定义
- `src/NanoBot.Core/Messages/MessageMetadata.cs` - 元数据定义
- `src/NanoBot.Core/Messages/MessageWithParts.cs` - 消息容器

### Agent 层
- `src/NanoBot.Agent/Messages/MessageAdapter.cs` - 消息格式适配器
- `src/NanoBot.Agent/AgentRuntime.cs` - 添加流式 Part 处理
- `src/NanoBot.Agent/Tools/PartAwareToolDecorator.cs` - 工具装饰器
- `src/NanoBot.Core/Services/IAgentRuntime.cs` - 接口更新

### WebUI
- `src/NanoBot.WebUI/Components/MessagePartComponent.razor` - Part 组件基类
- `src/NanoBot.WebUI/Components/TextPartComponent.razor` - 文本组件
- `src/NanoBot.WebUI/Components/ToolPartComponent.razor` - 工具组件
- `src/NanoBot.WebUI/Components/FilePartComponent.razor` - 文件组件
- `src/NanoBot.WebUI/Components/ReasoningPartComponent.razor` - 推理组件
- `src/NanoBot.WebUI/Components/ContextToolGroup.razor` - 工具分组
- `src/NanoBot.WebUI/Services/ToolIconService.cs` - 图标服务
- `src/NanoBot.WebUI/Hubs/MessagePartHub.cs` - SignalR Hub
- `src/NanoBot.WebUI/Services/MessagePartStreamer.cs` - 流服务

### 基础设施
- `src/NanoBot.Infrastructure/Serialization/MessagePartJsonConverter.cs` - JSON 序列化
- `src/NanoBot.Infrastructure/Memory/PartAwareHistoryStore.cs` - 历史存储

### 服务
- `src/NanoBot.Core/Services/ICostCalculator.cs` - 成本计算接口
- `src/NanoBot.Infrastructure/Services/CostCalculator.cs` - 成本计算实现
- `src/NanoBot.Providers/Decorators/TokenCountingChatClient.cs` - Token 统计

### DI 注册
- `src/NanoBot.Cli/Extensions/ServiceCollectionExtensions.cs` - 服务注册更新

---

## 6. 验收标准

### 6.1 功能验收

1. **Part 系统**
   - [ ] 支持 TextPart、ToolPart、FilePart、ReasoningPart
   - [ ] Part 可以正确序列化/反序列化
   - [ ] 消息可以包含多个 Part

2. **工具状态管理**
   - [ ] 工具调用状态流转: Pending → Running → Completed/Error
   - [ ] 状态变化可以实时通知前端
   - [ ] 支持工具执行过程中的元数据更新

3. **显示系统**
   - [ ] WebUI 可以显示不同类型的 Part
   - [ ] 工具组件支持折叠/展开
   - [ ] 上下文工具可以分组显示
   - [ ] 状态变化有视觉反馈（动画、图标变化）

4. **元数据跟踪**
   - [ ] 每个消息记录 Token 使用
   - [ ] 每个消息计算成本
   - [ ] 会话级成本统计可用

5. **历史记录**
   - [ ] 历史记录按新格式存储
   - [ ] 可以查询历史消息的 Part
   - [ ] 向后兼容旧格式

### 6.2 性能验收

- [ ] 流式更新延迟 < 100ms
- [ ] 历史记录查询性能不受影响
- [ ] 内存占用增加 < 20%

### 6.3 兼容性验收

- [ ] 现有 InboundMessage/OutboundMessage 接口向后兼容
- [ ] 现有频道（Telegram、Discord 等）不受影响
- [ ] 旧历史记录可以正常读取

---

## 7. 风险与缓解

| 风险 | 影响 | 可能性 | 缓解措施 |
|------|------|--------|----------|
| 向后兼容性问题 | 高 | 中 | 保持旧接口，使用适配器模式转换 |
| 性能下降 | 中 | 中 | 使用流式处理，避免内存复制 |
| 前端实现复杂 | 中 | 高 | 分阶段实现，先核心后增强 |
| SignalR 集成问题 | 中 | 低 | 预留轮询 fallback 方案 |
| 历史数据迁移 | 中 | 低 | 支持新旧格式并存，无需迁移 |

---

## 8. 下一步行动

1. **立即开始**: Phase 1 核心 Part 模型定义
2. **本周内**: 完成设计文档评审
3. **下周开始**: Phase 2 AgentRuntime 集成
4. **待 Phase 1-2 完成**: 评估是否调整 Phase 3-5 优先级

---

## 9. 相关文档

- [OpenCode 对比分析报告](../../reports/update/20250310-opencode-comparison.md)
- [WebUI 增强计划](./20250310-webui-enhancement-plan.md)
- [项目架构设计](../../solutions/design/)
- [Feature List](../../solutions/Feature-List.md)

---

## 10. 附录: 与 OpenCode 对比的改进点总结

| 特性 | OpenCode 实现 | NanoBot.Net 计划实现 | 状态 |
|------|---------------|---------------------|------|
| Part 系统 | Zod + TypeScript 类型安全 | C# 强类型 record | Phase 1 |
| 工具状态 | 4 种状态 + 实时更新 | 4 种状态 + SignalR 推送 | Phase 2-3 |
| Token 跟踪 | 内置 | IChatClient 装饰器 | Phase 4 |
| 成本计算 | 内置 | ICostCalculator 服务 | Phase 4 |
| 组件化显示 | SolidJS 组件 | Blazor 组件 | Phase 3 |
| 工具分组 | 智能上下文分组 | ContextToolGroup 组件 | Phase 3 |
| 实时同步 | WebSocket | SignalR | Phase 3 |
| 动画效果 | Motion 库 | CSS 动画 + Blazor | Phase 3 |
