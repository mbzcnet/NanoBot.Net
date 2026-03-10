using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Messages;

namespace NanoBot.Providers.Decorators;

/// <summary>
/// Token 统计装饰器 - 跟踪 LLM 调用的 Token 使用情况
/// </summary>
public class TokenCountingChatClient : IChatClient
{
    private readonly IChatClient _innerClient;
    private readonly ILogger<TokenCountingChatClient>? _logger;

    public TokenCountingChatClient(
        IChatClient innerClient,
        ILogger<TokenCountingChatClient>? logger = null)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
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
        if (response.Usage != null)
        {
            var usage = response.Usage;
            var tokenUsage = new TokenUsage
            {
                Input = (int)usage.InputTokenCount,
                Output = (int)(usage.OutputTokenCount ?? 0),
                Reasoning = GetAdditionalCount(usage, "reasoning"),
                Cache = GetCacheUsage(usage)
            };

            // 存储到异步本地上下文
            TokenUsageContext.Current = tokenUsage;

            _logger?.LogInformation(
                "Token usage - Input: {Input}, Output: {Output}, Reasoning: {Reasoning}, Total: {Total}",
                tokenUsage.Input,
                tokenUsage.Output,
                tokenUsage.Reasoning ?? 0,
                tokenUsage.Total);
        }

        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 流式响应的 Token 统计在响应完成后获取
        await foreach (var update in _innerClient.GetStreamingResponseAsync(
            messages, options, cancellationToken))
        {
            yield return update;
        }

        _logger?.LogDebug("Streaming response completed");
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return _innerClient.GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        (_innerClient as IDisposable)?.Dispose();
    }

    private static int? GetAdditionalCount(UsageDetails usage, string key)
    {
        if (usage.AdditionalCounts?.TryGetValue(key, out var value) == true)
        {
            return (int)value;
        }
        return null;
    }

    private static CacheTokenUsage? GetCacheUsage(UsageDetails usage)
    {
        long read = 0, write = 0;
        var hasRead = usage.AdditionalCounts?.TryGetValue("cache_read", out read) == true;
        var hasWrite = usage.AdditionalCounts?.TryGetValue("cache_write", out write) == true;

        if (hasRead || hasWrite)
        {
            return new CacheTokenUsage
            {
                Read = hasRead ? (int)read : 0,
                Write = hasWrite ? (int)write : 0
            };
        }

        return null;
    }
}

/// <summary>
/// 异步本地 Token 使用上下文
/// </summary>
public static class TokenUsageContext
{
    private static readonly AsyncLocal<TokenUsageHolder> _current = new();

    public static TokenUsage? Current
    {
        get => _current.Value?.Usage;
        set
        {
            var holder = _current.Value;
            if (holder == null)
            {
                holder = new TokenUsageHolder();
                _current.Value = holder;
            }
            holder.Usage = value;
        }
    }

    /// <summary>
    /// 清除当前上下文
    /// </summary>
    public static void Clear()
    {
        if (_current.Value != null)
        {
            _current.Value.Usage = null;
        }
    }

    private class TokenUsageHolder
    {
        public TokenUsage? Usage { get; set; }
    }
}
