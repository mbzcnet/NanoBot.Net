namespace NanoBot.Core.Tools.Rpa;

/// <summary>
/// RPA service interface - the main entry point for RPA capabilities.
/// </summary>
/// <remarks>
/// This interface combines execution, screen analysis, and health monitoring capabilities.
/// For specific capabilities, use <see cref="IRpaExecutor"/>, <see cref="IScreenAnalyzer"/>, or <see cref="IRpaHealthProvider"/>.
/// </remarks>
public interface IRpaService : IRpaExecutor, IScreenAnalyzer, IRpaHealthProvider, IDisposable
{
    /// <summary>
    /// Gets the screen size.
    /// </summary>
    Task<(int Width, int Height)> GetScreenSizeAsync();

    /// <summary>
    /// Gets the current cursor position.
    /// </summary>
    Task<(int X, int Y)> GetCursorPositionAsync();

    /// <summary>
    /// Starts the RPA service (e.g., OmniParser).
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the RPA service.
    /// </summary>
    Task StopAsync();
}

/// <summary>
/// RPA 健康状态
/// </summary>
public record RpaHealthStatus
{
    /// <summary>SharpHook 输入模拟是否可用</summary>
    public bool SharpHookAvailable { get; init; }

    /// <summary>OmniParser 视觉分析是否可用</summary>
    public bool OmniParserAvailable { get; init; }

    /// <summary>屏幕尺寸</summary>
    public (int Width, int Height)? ScreenSize { get; init; }

    /// <summary>SharpHook 错误信息</summary>
    public string? SharpHookError { get; init; }

    /// <summary>OmniParser 错误信息</summary>
    public string? OmniParserError { get; init; }

    /// <summary>状态描述</summary>
    public string StatusDescription => (SharpHookAvailable, OmniParserAvailable) switch
    {
        (true, true) => "Full RPA with Vision",
        (true, false) => "Basic RPA (OmniParser unavailable)",
        (false, _) => "RPA Unavailable"
    };
}
