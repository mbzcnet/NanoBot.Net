# NanoBot.Net 周同步方案 (2026-03-20)

本文档基于 Python 原项目周报 (2026-03-13 ~ 2026-03-20) 整理，记录 NanoBot.Net 与 Python 原项目的功能对齐状态和实现方案。

> **最近更新** (2026-03-20 实现):
> - 完成 7 个功能的实现对齐
> - Telegram 重试机制、配置增强
> - Cron 列表展示增强
> - Provider 空响应保护
> - Session 图片路径保留
> - Subagent 角色验证

---

## 对齐状态总览

| 功能 | Python 原项目 | NanoBot.Net | 状态 |
|------|--------------|-------------|------|
| Telegram 连接池分离 | ✅ 已实现 | ✅ 已实现 | ✅ 已对齐 |
| Telegram 超时重试 | ✅ 已实现 | ✅ 已实现 | ✅ 已对齐 |
| Telegram 远程媒体 URL | ✅ 已实现 | ✅ 已实现 | ✅ 已对齐 |
| Cron 列表展示增强 | ✅ 已实现 | ✅ 已实现 | ✅ 已对齐 |
| Provider 空 choices 处理 | ✅ 已实现 | ✅ 已实现 | ✅ 已对齐 |
| 图片路径保留 | ✅ 已实现 | ✅ 已实现 | ✅ 已对齐 |
| Subagent 角色验证 | ✅ 已实现 | ✅ 已实现 | ✅ 已对齐 |

---

## 1. Telegram 连接池分离

### 1.1 当前状态

NanoBot.Net 的 `TelegramChannel` 使用 `Telegram.Bot` 库，API 调用和轮询共享默认配置，可能在高并发下出现 "Pool timeout" 错误。

### 1.2 目标接口变更

**TelegramConfig 新增属性**:

```csharp
namespace NanoBot.Core.Configuration;

/// <summary>Telegram 通道配置</summary>
public class TelegramConfig
{
    // ... 现有属性 ...

    /// <summary>API 调用连接池大小（默认 32）</summary>
    public int ConnectionPoolSize { get; set; } = 32;

    /// <summary>连接池超时时间（秒，默认 30）</summary>
    public double PoolTimeout { get; set; } = 30.0;

    /// <summary>轮询连接池大小（默认 4）</summary>
    public int PollingPoolSize { get; set; } = 4;

    /// <summary>轮询超时时间（秒，默认 60）</summary>
    public double PollingTimeout { get; set; } = 60.0;
}
```

### 1.3 实现方案

**TelegramChannel 客户端分离**:

```csharp
// src/NanoBot.Channels/Telegram/TelegramChannel.cs
public class TelegramChannel : ChannelBase
{
    private readonly TelegramBotClient _apiClient;
    private readonly TelegramBotClient _pollingClient;

    public TelegramChannel(
        TelegramConfig config,
        IMessageBus messageBus)
    {
        // API 调用客户端 - 较大的连接池
        var apiClientOptions = new TelegramBotClientOptions(config.Token)
        {
            ConnectionTimeout = TimeSpan.FromSeconds(config.PoolTimeout)
        };
        _apiClient = new TelegramBotClient(apiClientOptions);

        // 轮询客户端 - 较小的连接池
        var pollingOptions = new TelegramBotClientOptions(config.Token)
        {
            ConnectionTimeout = TimeSpan.FromSeconds(config.PollingTimeout)
        };
        _pollingClient = new TelegramBotClient(pollingOptions);
    }

    private async Task<T> CallWithRetryAsync<T>(
        Func<Task<T>> func,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return await func();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
            catch (HttpRequestException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
        }

        return await func();
    }
}
```

---

## 2. Telegram 超时重试

### 2.1 当前状态

消息发送失败时缺乏重试机制，可能导致用户消息丢失。

### 2.2 目标接口变更

**CronTool 或 ITelegramChannel 新增方法**:

```csharp
// 扩展 ITelegramChannel 接口
public interface ITelegramChannel : IChannel
{
    // ... 现有方法 ...

    /// <summary>带重试的发送消息</summary>
    Task<long> SendMessageWithRetryAsync(
        OutboundMessage message,
        int maxRetries = 3,
        CancellationToken cancellationToken = default);

    /// <summary>带重试的发送媒体</summary>
    Task SendMediaWithRetryAsync(
        string chatId,
        string filePath,
        string? caption = null,
        int maxRetries = 3,
        CancellationToken cancellationToken = default);
}
```

### 2.3 实现方案

```csharp
// src/NanoBot.Channels/Telegram/TelegramChannel.cs
public async Task<long> SendMessageWithRetryAsync(
    OutboundMessage message,
    int maxRetries = 3,
    CancellationToken cancellationToken = default)
{
    var delay = TimeSpan.FromSeconds(1);
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        try
        {
            return await _apiClient.SendTextMessageAsync(
                message.ChatId,
                message.Content,
                parseMode: ParseMode.Html,
                replyToMessageId: message.ReplyTo != null
                    ? long.Parse(message.ReplyTo)
                    : null,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (attempt < maxRetries - 1)
        {
            await Task.Delay(delay, cancellationToken);
            delay *= 2; // 指数退避
        }
    }

    throw new TelegramException($"Failed after {maxRetries} attempts");
}
```

---

## 3. Telegram 远程媒体 URL

### 3.1 当前状态

`TelegramChannel` 主要支持本地文件路径发送媒体。

### 3.2 目标接口变更

**OutboundMessage Metadata 扩展**:

```csharp
// src/NanoBot.Core/Channels/OutboundMessage.cs
public record OutboundMessage
{
    // ... 现有属性 ...

    /// <summary>远程媒体 URL（可选）</summary>
    public IReadOnlyDictionary<string, string>? RemoteMediaUrls { get; init; }
}
```

**TelegramConfig 新增属性**:

```csharp
public class TelegramConfig
{
    // ... 现有属性 ...

    /// <summary>媒体下载超时时间（秒）</summary>
    public int MediaDownloadTimeout { get; set; } = 30;

    /// <summary>最大媒体文件大小（MB）</summary>
    public int MaxMediaFileSizeMb { get; set; } = 20;
}
```

### 3.3 实现方案

```csharp
// src/NanoBot.Channels/Telegram/TelegramChannel.cs
public async Task SendMediaFromUrlAsync(
    string chatId,
    string url,
    InputMediaType mediaType,
    string? caption = null,
    CancellationToken cancellationToken = default)
{
    // URL 验证
    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException($"Invalid media URL: {url}");
    }

    using var httpClient = new HttpClient();
    httpClient.Timeout = TimeSpan.FromSeconds(_config.MediaDownloadTimeout);

    var response = await httpClient.GetAsync(url, cancellationToken);
    if (response.StatusCode != HttpStatusCode.OK)
    {
        throw new HttpRequestException($"Failed to download media: {response.StatusCode}");
    }

    var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

    // 根据媒体类型发送
    switch (mediaType)
    {
        case InputMediaType.Photo:
            await _apiClient.SendPhotoAsync(chatId, stream, caption, cancellationToken: cancellationToken);
            break;
        case InputMediaType.Video:
            await _apiClient.SendVideoAsync(chatId, stream, caption, cancellationToken: cancellationToken);
            break;
        case InputMediaType.Document:
            await _apiClient.SendDocumentAsync(chatId, stream, caption, cancellationToken: cancellationToken);
            break;
    }
}
```

---

## 4. Cron 列表展示增强

### 4.1 当前状态

Cron 工具的 list 命令仅显示任务名称和基本状态，缺少详细的时间调度和运行状态信息。

### 4.2 目标接口变更

**CronJobDefinition 扩展**:

```csharp
namespace NanoBot.Core.Cron;

/// <summary>
/// Cron 任务定义
/// </summary>
public class CronJobDefinition
{
    // ... 现有属性 ...

    /// <summary>任务描述</summary>
    public string? Description { get; init; }

    /// <summary>任务标签（用于分类）</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Cron 任务状态
/// </summary>
public class CronJobState
{
    // ... 现有属性 ...

    /// <summary>上次运行错误信息</summary>
    public string? LastError { get; init; }

    /// <summary>运行次数统计</summary>
    public int RunCount { get; init; }

    /// <summary>成功次数统计</summary>
    public int SuccessCount { get; init; }

    /// <summary>失败次数统计</summary>
    public int FailureCount { get; init; }
}
```

**CronTool 输出格式化方法**:

```csharp
// src/NanoBot.Tools/Cron/CronTool.cs
public static class CronToolExtensions
{
    /// <summary>格式化调度时间详情</summary>
    public static string FormatTiming(CronJobDefinition definition)
    {
        return definition.ScheduleKind switch
        {
            "cron" => $"cron: {definition.CronExpression} ({definition.TimeZone})",
            "every" => $"every: {definition.IntervalSeconds}s",
            "at" => $"at: {definition.RunAt:yyyy-MM-ddTHH:mm:ssZ}",
            _ => $"unknown: {definition.ScheduleKind}"
        };
    }

    /// <summary>格式化任务运行状态</summary>
    public static string FormatState(CronJobState state)
    {
        var parts = new List<string>
        {
            $"enabled={state.Enabled}"
        };

        if (state.LastRun.HasValue)
        {
            parts.Add($"last_run={state.LastRun.Value:yyyy-MM-dd HH:mm:ss}");
            parts.Add($"last_status={state.Status}");
            if (!string.IsNullOrEmpty(state.LastError))
            {
                parts.Add($"error={state.LastError}");
            }
        }

        if (state.NextRun.HasValue)
        {
            parts.Add($"next_run={state.NextRun.Value:yyyy-MM-dd HH:mm:ss}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>格式化完整任务信息</summary>
    public static string FormatJobDetails(CronJob job)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  ID: {job.Id}");
        sb.AppendLine($"  Name: {job.Name}");
        sb.AppendLine($"  Timing: {FormatTiming(job.Definition)}");
        sb.AppendLine($"  State: {FormatState(job.State)}");
        if (job.Definition.Tags.Count > 0)
        {
            sb.AppendLine($"  Tags: {string.Join(", ", job.Definition.Tags)}");
        }
        return sb.ToString();
    }
}
```

---

## 5. Provider 空 choices 处理

### 5.1 当前状态

Custom provider 或其他 provider 返回空 choices 时可能导致异常。

### 5.2 目标接口变更

**ChatClientFactory 增强**:

```csharp
// src/NanoBot.Providers/ChatClientFactory.cs
public interface IChatClientFactory
{
    // ... 现有方法 ...

    /// <summary>创建带空响应保护的 ChatClient</summary>
    IChatClient CreateWithEmptyChoicesProtection(IChatClient innerClient);
}
```

### 5.3 实现方案

```csharp
// src/NanoBot.Providers/EmptyChoicesProtectionChatClient.cs
/// <summary>
/// 空 choices 保护客户端
/// </summary>
public class EmptyChoicesProtectionChatClient : IChatClient
{
    private readonly IChatClient _innerClient;

    public EmptyChoicesProtectionChatClient(IChatClient innerClient)
    {
        _innerClient = innerClient;
    }

    public async Task<ChatResponse> GetResponseAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _innerClient.GetResponseAsync(request, cancellationToken);

        // 检查并处理空 choices
        if (response.Choices.Count == 0)
        {
            return new ChatResponse
            {
                Choices = new List<ChatChoice>
                {
                    new ChatChoice
                    {
                        Text = string.Empty,
                        Index = 0,
                        FinishReason = "empty_response"
                    }
                },
                Usage = response.Usage,
                ModelId = response.ModelId
            };
        }

        return response;
    }

    // ... 其他 IChatClient 接口方法的委托实现 ...
}
```

**使用方式**:

```csharp
// src/NanoBot.Providers/ServiceCollectionExtensions.cs
public static IServiceCollection AddProviders(this IServiceCollection services)
{
    services.AddSingleton<IChatClientFactory, ChatClientFactory>();

    // 为所有创建的 client 添加空响应保护
    services.AddScoped<IChatClient>(sp =>
    {
        var factory = sp.GetRequiredService<IChatClientFactory>();
        var config = sp.GetRequiredService<LlmConfig>();

        var client = factory.CreateClient(config);
        return factory.CreateWithEmptyChoicesProtection(client);
    });

    return services;
}
```

---

## 6. 图片路径保留

### 6.1 当前状态

Session 管理中图片路径可能在 fallback 和 history 中丢失。

### 6.2 目标接口变更

**InboundMessage 扩展**:

```csharp
// src/NanoBot.Core/Channels/InboundMessage.cs
public record InboundMessage
{
    // ... 现有属性 ...

    /// <summary>原始图片路径列表（用于 session history）</summary>
    public IReadOnlyList<string> ImagePaths { get; init; } = Array.Empty<string>();

    /// <summary>保留图片路径到元数据</summary>
    public IDictionary<string, object> WithPreservedImagePaths()
    {
        var metadata = Metadata != null
            ? new Dictionary<string, object>(Metadata)
            : new Dictionary<string, object>();

        metadata["image_paths"] = ImagePaths;
        return metadata;
    }
}
```

**SessionManager 增强**:

```csharp
// src/NanoBot.Agent/SessionManager.cs
public partial class SessionManager
{
    /// <summary>保存消息时保留图片路径</summary>
    private void PreserveImagePaths(InboundMessage message, Session session)
    {
        if (message.ImagePaths.Count > 0)
        {
            var existingPaths = session.Metadata.ContainsKey("image_paths")
                ? session.Metadata["image_paths"] as List<string>
                : new List<string>();

            existingPaths?.AddRange(message.ImagePaths);
            session.Metadata["image_paths"] = existingPaths ?? new List<string>();
        }
    }

    /// <summary>从 session history 恢复图片路径</summary>
    public IReadOnlyList<string> GetImagePaths(Session session)
    {
        if (session.Metadata.TryGetValue("image_paths", out var paths))
        {
            return paths as List<string> ?? new List<string>();
        }
        return Array.Empty<string>();
    }
}
```

---

## 7. Subagent 角色验证

### 7.1 当前状态

需要验证 Subagent 结果消息的角色设置是否正确。

### 7.2 目标接口变更

**SubagentResult 扩展**:

```csharp
// src/NanoBot.Core/Subagents/SubagentResult.cs
namespace NanoBot.Core.Subagents;

/// <summary>
/// Subagent 执行结果
/// </summary>
public class SubagentResult
{
    // ... 现有属性 ...

    /// <summary>结果消息的角色（应始终为 assistant）</summary>
    public string Role { get; init; } = "assistant";

    /// <summary>验证角色是否正确</summary>
    public bool IsRoleValid => Role == "assistant";
}
```

### 7.3 实现方案

```csharp
// src/NanoBot.Infrastructure/SubagentManager.cs
public class SubagentManager : ISubagentManager
{
    public async Task<SubagentResult> RunSubagentAsync(
        SubagentInfo subagentInfo,
        string task,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteSubagentAsync(subagentInfo, task, cancellationToken);

        // 确保结果角色正确设置为 assistant
        if (result.Role != "assistant")
        {
            return result with { Role = "assistant" };
        }

        return result;
    }
}
```

---

## 变更汇总

| 模块 | 文件 | 变更类型 |
|------|------|----------|
| Core | `TelegramConfig.cs` | 新增连接池配置属性 |
| Core | `OutboundMessage.cs` | 新增 RemoteMediaUrls |
| Core | `InboundMessage.cs` | 新增 ImagePaths |
| Core | `CronJobDefinition.cs` | 新增 Description、Tags |
| Core | `CronJobState.cs` | 新增错误和统计字段 |
| Core | `SubagentResult.cs` | 新增 Role 验证 |
| Channels | `TelegramChannel.cs` | 分离连接池、重试机制、URL 发送 |
| Providers | `ChatClientFactory.cs` | 新增空响应保护 |
| Providers | `EmptyChoicesProtectionChatClient.cs` | 新建空响应保护客户端 |
| Tools | `CronTool.cs` | 新增格式化扩展方法 |
| Agent | `SessionManager.cs` | 新增图片路径保留 |

---

*文档版本：2026-03-20*
