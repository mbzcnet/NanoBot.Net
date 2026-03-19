using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using NanoBot.Core.Tools.Rpa;

namespace NanoBot.Infrastructure.Tools.Rpa.Linux;

/// <summary>
/// Linux 屏幕截图实现（基于 XLib）
/// </summary>
public class LinuxScreenCapture : IScreenCapture
{
    private readonly ILogger<LinuxScreenCapture>? _logger;

    public LinuxScreenCapture(ILogger<LinuxScreenCapture>? logger = null)
    {
        _logger = logger;
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XRootWindow(IntPtr display, int screen);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XGetImage(
        IntPtr display, IntPtr window, int x, int y, int width, int height,
        IntPtr planeMask, int format);

    [DllImport("libX11.so.6")]
    private static extern int XDestroyImage(IntPtr image);

    [DllImport("libX11.so.6")]
    private static extern int XGetGeometry(
        IntPtr display, IntPtr window, out IntPtr root, out int x, out int y,
        out uint width, out uint height, out uint borderWidth, out uint depth);

    private const int ZPixmap = 2;
    private static readonly IntPtr AllPlanes = new(-1);

    public Task<byte[]> CaptureAsync(int x = 0, int y = 0, int? width = null, int? height = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
                throw new InvalidOperationException("Failed to open X display");

            try
            {
                var screen = 0;
                var root = XRootWindow(display, screen);
                XGetGeometry(display, root, out _, out _, out _, out var screenWidth, out var screenHeight, out _, out _);

                var w = width ?? (int)screenWidth;
                var h = height ?? (int)screenHeight;

                var imagePtr = XGetImage(display, root, x, y, w, h, AllPlanes, ZPixmap);
                if (imagePtr == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to capture screen");

                try
                {
                    return ConvertXImageToPng(imagePtr, w, h);
                }
                finally
                {
                    XDestroyImage(imagePtr);
                }
            }
            finally
            {
                XCloseDisplay(display);
            }
        }, ct);
    }

    public Task<byte[]> CapturePrimaryScreenAsync(CancellationToken ct = default) => CaptureAsync(ct: ct);

    public (int Width, int Height) GetScreenSize()
    {
        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            return (1920, 1080);

        try
        {
            var screen = 0;
            var root = XRootWindow(display, screen);
            XGetGeometry(display, root, out _, out _, out _, out var width, out var height, out _, out _);
            return ((int)width, (int)height);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    public IReadOnlyList<ScreenInfo> GetAllScreens()
    {
        return new[] { new ScreenInfo("Primary Display", 0, 0, 0, 1920, 1080, 1.0f) };
    }

    private byte[] ConvertXImageToPng(IntPtr imagePtr, int width, int height)
    {
        // Get pixel data from XImage
        var pixelData = new byte[width * height * 4];
        Marshal.Copy(imagePtr, pixelData, 0, pixelData.Length);

        // X11 uses BGR format, we need to convert to RGB
        for (int i = 0; i < pixelData.Length; i += 4)
            (pixelData[i], pixelData[i + 2]) = (pixelData[i + 2], pixelData[i]);

        // Create SKBitmap from pixel data
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        
        // Copy pixel data into the bitmap
        var destSpan = bitmap.GetPixelSpan();
        pixelData.CopyTo(destSpan);

        // Encode to PNG
        using var ms = new MemoryStream();
        using var skImage = SKImage.FromBitmap(bitmap);
        skImage.Encode(SKEncodedImageFormat.Png, 100).SaveTo(ms);

        _logger?.LogDebug("Captured Linux screen: {Width}x{Height}", width, height);
        return ms.ToArray();
    }
}
