using System.Threading;

namespace NanoBot.Core.Tools;

public static class ToolExecutionContext
{
    private static readonly AsyncLocal<string?> _currentSessionKey = new();

    public static string? CurrentSessionKey => _currentSessionKey.Value;

    public static void SetCurrentSessionKey(string? sessionKey)
    {
        _currentSessionKey.Value = sessionKey;
    }
}
