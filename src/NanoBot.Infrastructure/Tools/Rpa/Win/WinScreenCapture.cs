#if NET10_0_OSX || NET10_0
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Tools.Rpa;

namespace NanoBot.Infrastructure.Tools.Rpa.Win;

/// <summary>
/// Windows 屏幕截图实现
/// </summary>
public class WinScreenCapture : IScreenCapture
{
    private readonly ILogger<WinScreenCapture>? _logger;

    public WinScreenCapture(ILogger<WinScreenCapture>? logger = null)
    {
        _logger = logger;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SRCCOPY = 0x00CC0020;

    public Task<byte[]> CaptureAsync(int x = 0, int y = 0, int? width = null, int? height = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var screenSize = GetScreenSize();
            var w = width ?? screenSize.Width;
            var h = height ?? screenSize.Height;

            var hdcScreen = GetDC(IntPtr.Zero);
            var hdcMem = CreateCompatibleDC(hdcScreen);
            var hBitmap = CreateCompatibleBitmap(hdcScreen, w, h);
            var hOld = SelectObject(hdcMem, hBitmap);

            BitBlt(hdcMem, 0, 0, w, h, hdcScreen, x, y, SRCCOPY);
            SelectObject(hdcMem, hOld);

            var result = ConvertBitmapToPng(hBitmap);

            DeleteObject(hBitmap);
            DeleteDC(hdcMem);
            ReleaseDC(IntPtr.Zero, hdcScreen);

            _logger?.LogDebug("Captured screen: {X},{Y} {Width}x{Height}", x, y, w, h);
            return result;
        }, ct);
    }

    public Task<byte[]> CapturePrimaryScreenAsync(CancellationToken ct = default) => CaptureAsync(ct: ct);

    public (int Width, int Height) GetScreenSize()
    {
        var width = GetSystemMetrics(SM_CXSCREEN);
        var height = GetSystemMetrics(SM_CYSCREEN);
        return (width, height);
    }

    public IReadOnlyList<ScreenInfo> GetAllScreens()
    {
        return new[] { new ScreenInfo("Primary Display", 0, 0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN), 1.0f) };
    }

    private byte[] ConvertBitmapToPng(IntPtr hBitmap)
    {
        using var gdiBitmap = Image.FromHbitmap(hBitmap);
        using var ms = new MemoryStream();
        gdiBitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
#else
using Microsoft.Extensions.Logging;
namespace NanoBot.Infrastructure.Tools.Rpa.Win;
public class WinScreenCapture : NanoBot.Core.Tools.Rpa.IScreenCapture
{
    public WinScreenCapture(ILogger? logger = null) => throw new PlatformNotSupportedException("WinScreenCapture is only supported on Windows");
    public Task<byte[]> CaptureAsync(int x = 0, int y = 0, int? width = null, int? height = null, CancellationToken ct = default) => throw new PlatformNotSupportedException("");
    public Task<byte[]> CapturePrimaryScreenAsync(CancellationToken ct = default) => throw new PlatformNotSupportedException("");
    public (int Width, int Height) GetScreenSize() => throw new PlatformNotSupportedException("");
    public IReadOnlyList<NanoBot.Core.Tools.Rpa.ScreenInfo> GetAllScreens() => throw new PlatformNotSupportedException("");
}
#endif
