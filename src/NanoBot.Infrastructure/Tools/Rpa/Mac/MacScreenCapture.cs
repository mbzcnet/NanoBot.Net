#if NET10_0_OSX
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using NanoBot.Core.Tools.Rpa;

namespace NanoBot.Infrastructure.Tools.Rpa.Mac;

/// <summary>
/// macOS 屏幕截图实现
/// </summary>
public class MacScreenCapture : IScreenCapture
{
    private readonly ILogger<MacScreenCapture>? _logger;

    public MacScreenCapture(ILogger<MacScreenCapture>? logger = null)
    {
        _logger = logger;
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGWindowListCreateImage(
        double x, double y, double width, double height,
        uint option, IntPtr windowID, uint imageOption);

    private const uint kCGNullWindowID = 0;
    private const uint kCGWindowListOptionIncludeWindow = 2;
    private const uint kCGWindowImageBoundsClamp = 4;

    public Task<byte[]> CaptureAsync(int x = 0, int y = 0, int? width = null, int? height = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var screenSize = GetScreenSize();
            var w = width ?? screenSize.Width;
            var h = height ?? screenSize.Height;

            var imagePtr = CGWindowListCreateImage(
                x, y, w, h,
                kCGWindowListOptionIncludeWindow,
                (IntPtr)kCGNullWindowID,
                kCGWindowImageBoundsClamp);

            if (imagePtr == IntPtr.Zero)
                throw new InvalidOperationException("Failed to capture screen");

            return ConvertCGImageToPng(imagePtr);
        }, ct);
    }

    public Task<byte[]> CapturePrimaryScreenAsync(CancellationToken ct = default) => CaptureAsync(ct: ct);

    public (int Width, int Height) GetScreenSize()
    {
        var mainScreen = CoreGraphics.CGScreen.Main;
        return ((int)mainScreen.Width, (int)mainScreen.Height);
    }

    public IReadOnlyList<ScreenInfo> GetAllScreens()
    {
        var screens = new List<ScreenInfo>();
        var allScreens = CoreGraphics.CGScreen.AllScreens;
        for (int i = 0; i < allScreens.Length; i++)
        {
            var screen = allScreens[i];
            screens.Add(new ScreenInfo(screen.DeviceName, i, (int)screen.Bounds.X, (int)screen.Bounds.Y,
                (int)screen.Width, (int)screen.Height, 1.0f));
        }
        return screens;
    }

    private byte[] ConvertCGImageToPng(IntPtr imagePtr)
    {
        using var cgImage = new CoreGraphics.CGImage(imagePtr);
        var width = cgImage.Width;
        var height = cgImage.Height;
        var bytesPerRow = width * 4;
        var colorSpace = CoreGraphics.CGColorSpace.CreateDeviceRGB();
        using var context = new CoreGraphics.CGBitmapContext(
            null, width, height, 8, bytesPerRow, colorSpace,
            CoreGraphics.CGBitmapInfo.ByteOrder32Big | CoreGraphics.CGBitmapInfo.PremultipliedLast);
        context.Draw(new CoreGraphics.CGPoint(0, 0), cgImage);
        var pixels = new byte[width * height * 4];
        Marshal.Copy(context.Data, pixels, 0, pixels.Length);
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        unsafe
        {
            fixed (byte* ptr = pixels)
            {
                var span = new Span<byte>(ptr, pixels.Length);
                for (int i = 0; i < span.Length; i += 4)
                    (span[i], span[i + 2]) = (span[i + 2], span[i]);
            }
        }
        using var ms = new MemoryStream();
        using var skImage = SKImage.FromBitmap(bitmap);
        skImage.Encode(SKEncodedImageFormat.Png, 100).SaveTo(ms);
        return ms.ToArray();
    }
}
#else
using Microsoft.Extensions.Logging;
namespace NanoBot.Infrastructure.Tools.Rpa.Mac;
public class MacScreenCapture : NanoBot.Core.Tools.Rpa.IScreenCapture
{
    public MacScreenCapture(ILogger? logger = null) => throw new PlatformNotSupportedException("MacScreenCapture is only supported on macOS");
    public Task<byte[]> CaptureAsync(int x = 0, int y = 0, int? width = null, int? height = null, CancellationToken ct = default) => throw new PlatformNotSupportedException("");
    public Task<byte[]> CapturePrimaryScreenAsync(CancellationToken ct = default) => throw new PlatformNotSupportedException("");
    public (int Width, int Height) GetScreenSize() => throw new PlatformNotSupportedException("");
    public IReadOnlyList<NanoBot.Core.Tools.Rpa.ScreenInfo> GetAllScreens() => throw new PlatformNotSupportedException("");
}
#endif
