using Microsoft.Extensions.Logging;
using NanoBot.Core.Tools.Rpa;

namespace NanoBot.Infrastructure.Tools.Rpa;

/// <summary>
/// 屏幕截图服务
/// </summary>
public class ScreenCaptureService : IScreenCapture
{
    private readonly IScreenCapture _platformCapture;
    private readonly ILogger<ScreenCaptureService>? _logger;

    public ScreenCaptureService(IScreenCapture platformCapture, ILogger<ScreenCaptureService>? logger = null)
    {
        _platformCapture = platformCapture;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<byte[]> CaptureAsync(
        int x = 0,
        int y = 0,
        int? width = null,
        int? height = null,
        CancellationToken ct = default)
    {
        return await _platformCapture.CaptureAsync(x, y, width, height, ct);
    }

    /// <inheritdoc />
    public async Task<byte[]> CapturePrimaryScreenAsync(CancellationToken ct = default)
    {
        return await _platformCapture.CapturePrimaryScreenAsync(ct);
    }

    /// <inheritdoc />
    public (int Width, int Height) GetScreenSize()
    {
        return _platformCapture.GetScreenSize();
    }

    /// <inheritdoc />
    public IReadOnlyList<ScreenInfo> GetAllScreens()
    {
        return _platformCapture.GetAllScreens();
    }

    /// <summary>
    /// 截取并优化（用于 OmniParser）
    /// </summary>
    public async Task<OptimizedImage> CaptureAndOptimizeAsync(
        ImageOptimizer optimizer,
        OmniParserOptimizationOptions? options = null,
        CancellationToken ct = default)
    {
        var rawImage = await CapturePrimaryScreenAsync(ct);
        return await optimizer.OptimizeForOmniParserAsync(rawImage, options, ct);
    }
}

/// <summary>
/// 截图服务工厂
/// </summary>
public static class ScreenCaptureFactory
{
    /// <summary>
    /// 创建平台特定的截图服务
    /// </summary>
    public static IScreenCapture Create()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new MacScreenCaptureStub();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WinScreenCaptureStub();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxScreenCaptureStub();
        }

        throw new NotSupportedException($"Platform {Environment.OSVersion} is not supported");
    }
}

/// <summary>
/// macOS 截图存根 - 在非 macOS 平台返回占位符
/// </summary>
public class MacScreenCaptureStub : IScreenCapture
{
    public Task<byte[]> CaptureAsync(int x = 0, int y = 0, int? width = null, int? height = null, CancellationToken ct = default)
    {
        // 返回一个 1x1 透明 PNG
        return Task.FromResult(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 });
    }

    public Task<byte[]> CapturePrimaryScreenAsync(CancellationToken ct = default)
        => CaptureAsync(ct: ct);

    public (int Width, int Height) GetScreenSize()
        => (1920, 1080);

    public IReadOnlyList<ScreenInfo> GetAllScreens()
        => new[] { new ScreenInfo("Primary Display", 0, 0, 0, 1920, 1080, 1.0f) };
}

/// <summary>
/// Windows 截图存根 - 在非 Windows 平台返回占位符
/// </summary>
public class WinScreenCaptureStub : IScreenCapture
{
    public Task<byte[]> CaptureAsync(int x = 0, int y = 0, int? width = null, int? height = null, CancellationToken ct = default)
    {
        // 返回一个 1x1 透明 PNG
        return Task.FromResult(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 });
    }

    public Task<byte[]> CapturePrimaryScreenAsync(CancellationToken ct = default)
        => CaptureAsync(ct: ct);

    public (int Width, int Height) GetScreenSize()
        => (1920, 1080);

    public IReadOnlyList<ScreenInfo> GetAllScreens()
        => new[] { new ScreenInfo("Primary Display", 0, 0, 0, 1920, 1080, 1.0f) };
}

/// <summary>
/// Linux 截图存根 - 在非 Linux 平台返回占位符
/// </summary>
public class LinuxScreenCaptureStub : IScreenCapture
{
    public Task<byte[]> CaptureAsync(int x = 0, int y = 0, int? width = null, int? height = null, CancellationToken ct = default)
    {
        // 返回一个 1x1 透明 PNG
        return Task.FromResult(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 });
    }

    public Task<byte[]> CapturePrimaryScreenAsync(CancellationToken ct = default)
        => CaptureAsync(ct: ct);

    public (int Width, int Height) GetScreenSize()
        => (1920, 1080);

    public IReadOnlyList<ScreenInfo> GetAllScreens()
        => new[] { new ScreenInfo("Primary Display", 0, 0, 0, 1920, 1080, 1.0f) };
}
