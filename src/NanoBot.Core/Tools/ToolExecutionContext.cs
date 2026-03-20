using System.Collections.Concurrent;
using System.Threading;

namespace NanoBot.Core.Tools;

public static class ToolExecutionContext
{
    // AsyncLocal 用于在同一线程的异步链中传递值
    private static readonly AsyncLocal<string?> _currentSessionKey = new();
    
    // 使用 ConcurrentDictionary 作为 fallback，以线程 ID 为键
    // 这是因为 Microsoft.Agents.AI 库在调用工具时可能在不同的异步上下文中执行
    private static readonly ConcurrentDictionary<int, string?> _threadSessionKeys = new();
    
    // 全局 session key，作为所有线程的 fallback
    // 当工具在不同的线程上执行时，使用这个值
    private static string? _globalSessionKey;
    private static readonly object _globalLock = new();

    public static string? CurrentSessionKey => 
        _currentSessionKey.Value ?? 
        _threadSessionKeys.GetValueOrDefault(Environment.CurrentManagedThreadId) ??
        _globalSessionKey;

    public static void SetCurrentSessionKey(string? sessionKey)
    {
        _currentSessionKey.Value = sessionKey;
        
        var threadId = Environment.CurrentManagedThreadId;
        if (sessionKey != null)
        {
            _threadSessionKeys[threadId] = sessionKey;
            lock (_globalLock)
            {
                _globalSessionKey = sessionKey;
            }
        }
        else
        {
            _threadSessionKeys.TryRemove(threadId, out _);
            lock (_globalLock)
            {
                _globalSessionKey = null;
            }
        }
    }
}
