# OpenCode vs NanoBot.Net 架构对比分析

## 概述

本文档对比分析了 OpenCode 和 NanoBot.Net 在消息格式、工具调用格式和显示样式处理方面的设计差异，为 NanoBot.Net 的优化提供参考。

## 1. 消息格式对比

### OpenCode 消息格式

OpenCode 采用了基于 Zod 的类型安全消息系统，具有以下特点：

#### 消息结构 (MessageV2)
```typescript
export const Info = z.discriminatedUnion("role", [User, Assistant])
export type Info = z.infer<typeof Info>

// 用户消息
export const User = Base.extend({
  role: z.literal("user"),
  time: z.object({ created: z.number() }),
  format: Format.optional(),
  summary: z.object({
    title: z.string().optional(),
    body: z.string().optional(),
    diffs: Snapshot.FileDiff.array(),
  }).optional(),
  agent: z.string(),
  model: z.object({
    providerID: z.string(),
    modelID: z.string(),
  }),
  system: z.string().optional(),
  tools: z.record(z.string(), z.boolean()).optional(),
  variant: z.string().optional(),
})

// 助手消息
export const Assistant = Base.extend({
  role: z.literal("assistant"),
  time: z.object({
    created: z.number(),
    completed: z.number().optional(),
  }),
  error: z.discriminatedUnion("name", [...]).optional(),
  parentID: z.string(),
  modelID: z.string(),
  providerID: z.string(),
  mode: z.string(),
  agent: z.string(),
  path: z.object({
    cwd: z.string(),
    root: z.string(),
  }),
  summary: z.boolean().optional(),
  cost: z.number(),
  tokens: z.object({
    total: z.number().optional(),
    input: z.number(),
    output: z.number(),
    reasoning: z.number(),
    cache: z.object({
      read: z.number(),
      write: z.number(),
    }),
  }),
  structured: z.any().optional(),
  variant: z.string().optional(),
  finish: z.string().optional(),
})
```

#### 消息部分 (Part) 系统
OpenCode 使用了高度模块化的 Part 系统：

```typescript
export const Part = z.discriminatedUnion("type", [
  TextPart,           // 文本内容
  SubtaskPart,        // 子任务
  ReasoningPart,      // 推理过程
  FilePart,           // 文件附件
  ToolPart,           // 工具调用
  StepStartPart,      // 步骤开始
  StepFinishPart,     // 步骤结束
  SnapshotPart,       // 快照
  PatchPart,          // 补丁
  AgentPart,          // 代理
  RetryPart,          // 重试
  CompactionPart,     // 压缩
])
```

#### 工具调用状态管理
```typescript
export const ToolState = z.discriminatedUnion("status", [
  ToolStatePending,    // 等待中
  ToolStateRunning,    // 运行中
  ToolStateCompleted,  // 已完成
  ToolStateError       // 错误
])

export const ToolPart = PartBase.extend({
  type: z.literal("tool"),
  callID: z.string(),
  tool: z.string(),
  state: ToolState,
  metadata: z.record(z.string(), z.any()).optional(),
})
```

### NanoBot.Net 消息格式

NanoBot.Net 采用了更简洁的消息结构：

#### 消息结构
```csharp
// 入站消息
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

// 出站消息
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

#### 工具定义
```csharp
public static AITool CreateReadFileTool(string? allowedDir = null)
{
    return AIFunctionFactory.Create(
        (string path, int? startLine, int? endLine, CancellationToken cancellationToken) =>
            ReadFileAsync(path, startLine, endLine, allowedDir, cancellationToken),
        new AIFunctionFactoryOptions
        {
            Name = "read_file",
            Description = "Read the contents of a file at the given path. Returns the file content as a string."
        });
}
```

### 对比分析

| 特性 | OpenCode | NanoBot.Net | 优势 |
|------|----------|-------------|------|
| **类型安全** | Zod 强类型系统 | C# 强类型 | 相当 |
| **消息结构** | 模块化 Part 系统 | 简单字符串内容 | OpenCode |
| **状态管理** | 详细的工具状态跟踪 | 基础状态 | OpenCode |
| **元数据支持** | 丰富的元数据字段 | 基础元数据 | OpenCode |
| **成本跟踪** | 内置 token 和成本跟踪 | 无 | OpenCode |
| **错误处理** | 结构化错误类型 | 异常处理 | OpenCode |

## 2. 工具调用格式对比

### OpenCode 工具系统

#### 工具定义接口
```typescript
export interface Info<Parameters extends z.ZodType = z.ZodType, M extends Metadata = Metadata> {
  id: string
  init: (ctx?: InitContext) => Promise<{
    description: string
    parameters: Parameters
    execute(
      args: z.infer<Parameters>,
      ctx: Context,
    ): Promise<{
      title: string
      metadata: M
      output: string
      attachments?: Omit<MessageV2.FilePart, "id" | "sessionID" | "messageID">[]
    }>
    formatValidationError?(error: z.ZodError): string
  }>
}
```

#### 工具执行上下文
```typescript
export type Context<M extends Metadata = Metadata> = {
  sessionID: string
  messageID: string
  agent: string
  abort: AbortSignal
  callID?: string
  extra?: { [key: string]: any }
  messages: MessageV2.WithParts[]
  metadata(input: { title?: string; metadata?: M }): void
  ask(input: Omit<PermissionNext.Request, "id" | "sessionID" | "tool">): Promise<void>
}
```

#### 工具状态管理
OpenCode 提供了详细的工具状态跟踪：

- **Pending**: 工具调用已创建但未开始执行
- **Running**: 工具正在执行中，支持实时元数据更新
- **Completed**: 工具执行完成，包含输出和附件
- **Error**: 工具执行失败，包含错误信息

### NanoBot.Net 工具系统

#### 工具定义
```csharp
public static AITool CreateReadFileTool(string? allowedDir = null)
{
    return AIFunctionFactory.Create(
        (string path, int? startLine, int? endLine, CancellationToken cancellationToken) =>
            ReadFileAsync(path, startLine, endLine, allowedDir, cancellationToken),
        new AIFunctionFactoryOptions
        {
            Name = "read_file",
            Description = "Read the contents of a file at the given path. Returns the file content as a string."
        });
}
```

#### 工具执行
NanoBot.Net 使用 Microsoft.Extensions.AI 框架：

```csharp
private static async Task<string> ReadFileAsync(
    string path,
    int? startLine,
    int? endLine,
    string? allowedDir,
    CancellationToken cancellationToken)
{
    // 实现逻辑
}
```

### 对比分析

| 特性 | OpenCode | NanoBot.Net | 优势 |
|------|----------|-------------|------|
| **参数验证** | Zod 自动验证 | 手动验证 | OpenCode |
| **状态跟踪** | 详细状态管理 | 基础执行 | OpenCode |
| **附件支持** | 内置附件系统 | 媒体列表 | OpenCode |
| **错误格式化** | 自定义错误格式化 | 标准异常 | OpenCode |
| **权限管理** | 内置权限询问 | 无 | OpenCode |
| **元数据更新** | 实时元数据更新 | 静态元数据 | OpenCode |

## 3. 显示样式处理对比

### OpenCode 显示系统

#### 组件化设计
OpenCode 使用 SolidJS 的组件化设计：

```typescript
export function Message(props: MessageProps) {
  return (
    <Switch>
      <Match when={props.message.role === "user" && props.message}>
        {(userMessage) => (
          <UserMessageDisplay
            message={userMessage() as UserMessage}
            parts={props.parts}
            interrupted={props.interrupted}
            queued={props.queued}
          />
        )}
      </Match>
      <Match when={props.message.role === "assistant" && props.message}>
        {(assistantMessage) => (
          <AssistantMessageDisplay
            message={assistantMessage() as AssistantMessage}
            parts={props.parts}
            showAssistantCopyPartID={props.showAssistantCopyPartID}
            showReasoningSummaries={props.showReasoningSummaries}
          />
        )}
      </Match>
    </Switch>
  )
}
```

#### 工具信息映射
```typescript
export function getToolInfo(tool: string, input: any = {}): ToolInfo {
  const i18n = useI18n()
  switch (tool) {
    case "read":
      return {
        icon: "glasses",
        title: i18n.t("ui.tool.read"),
        subtitle: input.filePath ? getFilename(input.filePath) : undefined,
      }
    case "list":
      return {
        icon: "bullet-list",
        title: i18n.t("ui.tool.list"),
        subtitle: input.path ? getFilename(input.path) : undefined,
      }
    // ... 更多工具
  }
}
```

#### 上下文工具分组
OpenCode 支持将相关工具（如 read、glob、grep、list）分组显示：

```typescript
function ContextToolGroup(props: { parts: ToolPart[]; busy?: boolean }) {
  const summary = createMemo(() => contextToolSummary(props.parts))
  
  return (
    <Collapsible open={open()} onOpenChange={setOpen} variant="ghost">
      <Collapsible.Trigger>
        <div data-component="context-tool-group-trigger">
          <ToolStatusTitle
            active={pending()}
            activeText={i18n.t("ui.sessionTurn.status.gatheringContext")}
            doneText={i18n.t("ui.sessionTurn.status.gatheredContext")}
            split={false}
          />
          <AnimatedCountList
            items={[
              { key: "read", count: summary().read, ... },
              { key: "search", count: summary().search, ... },
              { key: "list", count: summary().list, ... },
            ]}
          />
        </div>
      </Collapsible.Trigger>
    </Collapsible>
  )
}
```

#### 动画和交互
- 使用 Motion 库实现流畅动画
- 支持实时状态更新和闪烁效果
- 提供丰富的交互反馈

### NanoBot.Net 显示系统

NanoBot.Net 主要基于 WebUI 的显示：

#### 基础消息显示
```csharp
// 主要通过 Content 字段显示
public required string Content { get; init; }
public IReadOnlyList<string> Media { get; init; } = Array.Empty<string>();
```

#### 工具调用显示
工具调用结果通常直接嵌入在消息内容中，没有专门的组件化显示系统。

### 对比分析

| 特性 | OpenCode | NanoBot.Net | 优势 |
|------|----------|-------------|------|
| **组件化** | 高度组件化 | 基础模板 | OpenCode |
| **工具分组** | 智能上下文分组 | 无 | OpenCode |
| **国际化** | 完整 i18n 支持 | 有限 | OpenCode |
| **动画效果** | 流畅动画交互 | 静态显示 | OpenCode |
| **状态指示** | 实时状态指示 | 基础状态 | OpenCode |
| **图标系统** | 统一图标映射 | 有限图标 | OpenCode |

## 4. 建议改进方案

### 4.1 消息格式改进

#### 引入 Part 系统
```csharp
public abstract record MessagePart
{
    public required string Id { get; init; }
    public required string MessageId { get; init; }
    public required string SessionId { get; init; }
}

public record TextPart : MessagePart
{
    public required string Text { get; init; }
    public bool Synthetic { get; init; }
    public bool Ignored { get; init; }
    public TimeRange? Time { get; init; }
}

public record ToolPart : MessagePart
{
    public required string CallId { get; init; }
    public required string Tool { get; init; }
    public required ToolState State { get; init; }
}
```

#### 增强元数据支持
```csharp
public record MessageMetadata
{
    public TimeInfo Time { get; init; }
    public ErrorInfo? Error { get; init; }
    public TokenUsage Tokens { get; init; }
    public decimal Cost { get; init; }
    public ModelInfo Model { get; init; }
}
```

### 4.2 工具系统改进

#### 工具状态管理
```csharp
public abstract record ToolState
{
    public required string CallId { get; init; }
    public required Dictionary<string, object> Input { get; init; }
    public TimeInfo Time { get; init; }
}

public record PendingToolState : ToolState
{
    public required string Raw { get; init; }
}

public record RunningToolState : ToolState
{
    public string? Title { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public record CompletedToolState : ToolState
{
    public required string Output { get; init; }
    public required string Title { get; init; }
    public List<FileAttachment> Attachments { get; init; } = new();
}
```

#### 工具执行上下文
```csharp
public record ToolContext
{
    public required string SessionId { get; init; }
    public required string MessageId { get; init; }
    public required string Agent { get; init; }
    public required CancellationToken AbortSignal { get; init; }
    public string? CallId { get; init; }
    public List<MessageWithParts> Messages { get; init; } = new();
    
    public Func<ToolMetadata, Task> SetMetadata { get; init; }
    public Func<PermissionRequest, Task> AskPermission { get; init; }
}
```

### 4.3 显示系统改进

#### 组件化设计
```typescript
// 前端组件改进
interface MessagePartProps {
  part: MessagePart
  message: Message
  hideDetails?: boolean
  defaultOpen?: boolean
}

export function ToolPartComponent(props: MessagePartProps) {
  const toolInfo = getToolInfo(props.part.tool, props.part.state.input)
  
  return (
    <Collapsible open={props.defaultOpen}>
      <Collapsible.Trigger>
        <ToolIcon name={toolInfo.icon} />
        <ToolTitle title={toolInfo.title} subtitle={toolInfo.subtitle} />
        <ToolStatus state={props.part.state.status} />
      </Collapsible.Trigger>
      <Collapsible.Content>
        <ToolOutput state={props.part.state} />
      </Collapsible.Content>
    </Collapsible>
  )
}
```

#### 工具分组显示
```typescript
function ContextToolGroup({ parts }: { parts: ToolPart[] }) {
  const summary = useMemo(() => ({
    read: parts.filter(p => p.tool === 'read_file').length,
    search: parts.filter(p => p.tool === 'grep' || p.tool === 'glob').length,
    list: parts.filter(p => p.tool === 'list_dir').length,
  }), [parts])

  return (
    <ToolGroup>
      <ToolGroupHeader>
        <ToolGroupIcon />
        <ToolGroupTitle>Gathering Context</ToolGroupTitle>
        <ToolGroupSummary summary={summary} />
      </ToolGroupHeader>
      <ToolGroupList>
        {parts.map(part => <ToolPartComponent key={part.id} part={part} />)}
      </ToolGroupList>
    </ToolGroup>
  )
}
```

## 5. 实施优先级

### 高优先级
1. **引入 Part 系统** - 提供更灵活的消息结构
2. **工具状态管理** - 实现详细的工具执行跟踪
3. **组件化显示** - 改善用户体验

### 中优先级
1. **元数据增强** - 添加成本和 token 跟踪
2. **工具分组** - 智能上下文分组显示
3. **错误处理改进** - 结构化错误信息

### 低优先级
1. **动画效果** - 提升视觉体验
2. **国际化支持** - 多语言支持
3. **权限管理** - 工具执行权限控制

## 6. 结论

OpenCode 在消息格式、工具调用和显示样式方面都展现了更先进的设计理念：

1. **模块化设计** - Part 系统提供了高度的消息结构灵活性
2. **状态管理** - 详细的工具状态跟踪提供了更好的用户体验
3. **组件化显示** - 丰富的 UI 组件提供了更好的交互体验

NanoBot.Net 可以通过引入这些设计理念来显著改善其架构和用户体验。建议优先实施 Part 系统和工具状态管理，这些改进将为后续的功能扩展奠定良好基础。
