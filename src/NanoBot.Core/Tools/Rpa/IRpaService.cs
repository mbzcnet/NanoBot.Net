namespace NanoBot.Core.Tools.Rpa;

/// <summary>
/// RPA 服务接口
/// </summary>
public interface IRpaService
{
    /// <summary>
    /// 执行操作流程
    /// </summary>
    Task<RpaFlowResult> ExecuteFlowAsync(RpaFlowRequest request, CancellationToken ct = default);

    /// <summary>
    /// 执行单个操作
    /// </summary>
    Task ExecuteActionAsync(RpaAction action, CancellationToken ct = default);

    /// <summary>
    /// 获取屏幕尺寸
    /// </summary>
    Task<(int Width, int Height)> GetScreenSizeAsync();

    /// <summary>
    /// 获取当前鼠标位置
    /// </summary>
    Task<(int X, int Y)> GetCursorPositionAsync();

    /// <summary>
    /// 截图并调用 OmniParser 分析
    /// </summary>
    Task<OmniParserResult> AnalyzeScreenAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取服务健康状态
    /// </summary>
    Task<RpaHealthStatus> GetHealthStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// 启动服务（启动 OmniParser 服务等）
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// 停止服务
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
