using System.Text.Encodings.Web;
using System.Text.Json;
using NanoBot.Core.Tools;

namespace NanoBot.Tools.BuiltIn;

public static partial class BrowserTools
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void SetCurrentSessionKey(string? sessionKey)
    {
        ToolExecutionContext.SetCurrentSessionKey(sessionKey);
    }

    internal static string ResolveSessionKey(string? sessionKey, Func<string?>? sessionKeyProvider)
    {
        return !string.IsNullOrWhiteSpace(sessionKey)
            ? sessionKey.Trim()
            : (sessionKeyProvider?.Invoke() ?? ToolExecutionContext.CurrentSessionKey ?? string.Empty);
    }

    internal static string Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required");
        }

        return value.Trim();
    }
}
