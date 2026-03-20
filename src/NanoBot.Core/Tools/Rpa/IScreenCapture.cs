namespace NanoBot.Core.Tools.Rpa;

/// <summary>
/// 屏幕截图接口
/// </summary>
public interface IScreenCapture
{
    /// <summary>
    /// 截取指定区域的屏幕
    /// </summary>
    /// <param name="x">起始 X 坐标</param>
    /// <param name="y">起始 Y 坐标</param>
    /// <param name="width">截取宽度，null 表示到屏幕边缘</param>
    /// <param name="height">截取高度，null 表示到屏幕边缘</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>PNG 格式的图像字节数组</returns>
    Task<byte[]> CaptureAsync(
        int x = 0,
        int y = 0,
        int? width = null,
        int? height = null,
        CancellationToken ct = default);

    /// <summary>
    /// 截取主屏幕
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>PNG 格式的图像字节数组</returns>
    Task<byte[]> CapturePrimaryScreenAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取主屏幕尺寸
    /// </summary>
    (int Width, int Height) GetScreenSize();

    /// <summary>
    /// 获取所有屏幕信息
    /// </summary>
    IReadOnlyList<ScreenInfo> GetAllScreens();
}

/// <summary>
/// 屏幕信息
/// </summary>
public record ScreenInfo(
    string Name,
    int Index,
    int X,
    int Y,
    int Width,
    int Height,
    float ScaleFactor);
