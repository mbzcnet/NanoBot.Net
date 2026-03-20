using Microsoft.Extensions.Logging;
using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;
using NanoBot.Core.Tools.Rpa;

namespace NanoBot.Infrastructure.Tools.Rpa;

/// <summary>
/// 基于 SharpHook 的输入模拟器实现
/// </summary>
public class SharpHookInputSimulator : IInputSimulator, IDisposable
{
    private readonly ILogger<SharpHookInputSimulator>? _logger;
    private readonly IEventSimulator _simulator;
    private bool _disposed;

    public SharpHookInputSimulator(ILogger<SharpHookInputSimulator>? logger = null)
    {
        _logger = logger;
        _simulator = new EventSimulator();
    }

    /// <inheritdoc />
    public Task MoveMouseAsync(int x, int y, int? durationMs = null)
    {
        if (durationMs.HasValue && durationMs.Value > 0)
        {
            return MoveMouseAnimatedAsync(x, y, durationMs.Value);
        }

        return Task.Run(() =>
        {
            _simulator.SimulateMouseMovement((short)x, (short)y);
            _logger?.LogDebug("Mouse moved to ({X}, {Y})", x, y);
        });
    }

    private Task MoveMouseAnimatedAsync(int x, int y, int durationMs)
    {
        return Task.Run(async () =>
        {
            var startPos = GetCursorPosition();
            var steps = Math.Max(10, durationMs / 20);

            for (int i = 1; i <= steps; i++)
            {
                var progress = (double)i / steps;
                var easeProgress = EaseInOutCubic(progress);
                var currentX = (int)(startPos.X + (x - startPos.X) * easeProgress);
                var currentY = (int)(startPos.Y + (y - startPos.Y) * easeProgress);

                _simulator.SimulateMouseMovement((short)currentX, (short)currentY);
                await Task.Delay(durationMs / steps);
            }

            _logger?.LogDebug("Mouse animated move to ({X}, {Y}) in {Duration}ms", x, y, durationMs);
        });
    }

    private static double EaseInOutCubic(double t) =>
        t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <inheritdoc />
    public Task ClickAsync(RpaMouseButton button = RpaMouseButton.Left)
    {
        return Task.Run(() =>
        {
            var pos = GetCursorPosition();
            _simulator.SimulateMousePress((short)pos.X, (short)pos.Y, ConvertMouseButton(button));
            _simulator.SimulateMouseRelease((short)pos.X, (short)pos.Y, ConvertMouseButton(button));
            _logger?.LogDebug("Clicked with {Button} at ({X}, {Y})", button, pos.X, pos.Y);
        });
    }

    /// <inheritdoc />
    public Task DoubleClickAsync(RpaMouseButton button = RpaMouseButton.Left)
    {
        return Task.Run(async () =>
        {
            var pos = GetCursorPosition();
            var mouseBtn = ConvertMouseButton(button);
            
            _simulator.SimulateMousePress((short)pos.X, (short)pos.Y, mouseBtn, 2);
            _simulator.SimulateMouseRelease((short)pos.X, (short)pos.Y, mouseBtn, 2);
            _logger?.LogDebug("Double clicked with {Button} at ({X}, {Y})", button, pos.X, pos.Y);
        });
    }

    /// <inheritdoc />
    public Task RightClickAsync()
    {
        return Task.Run(() =>
        {
            var pos = GetCursorPosition();
            _simulator.SimulateMousePress((short)pos.X, (short)pos.Y, MouseButton.Button2);
            _simulator.SimulateMouseRelease((short)pos.X, (short)pos.Y, MouseButton.Button2);
            _logger?.LogDebug("Right clicked at ({X}, {Y})", pos.X, pos.Y);
        });
    }

    /// <inheritdoc />
    public Task DragAsync(int fromX, int fromY, int toX, int toY, int? durationMs = null)
    {
        return Task.Run(async () =>
        {
            var mouseBtn = MouseButton.Button1;

            _simulator.SimulateMouseMovement((short)fromX, (short)fromY);
            _simulator.SimulateMousePress((short)fromX, (short)fromY, mouseBtn);
            
            await MoveMouseAnimatedAsync(toX, toY, durationMs ?? 500);

            _simulator.SimulateMouseRelease((short)toX, (short)toY, mouseBtn);

            _logger?.LogDebug("Dragged from ({FromX}, {FromY}) to ({ToX}, {ToY})",
                fromX, fromY, toX, toY);
        });
    }

    /// <inheritdoc />
    public Task TypeTextAsync(string text)
    {
        return Task.Run(() =>
        {
            _simulator.SimulateTextEntry(text);
            _logger?.LogDebug("Typed text: {Text}", text);
        });
    }

    /// <inheritdoc />
    public Task PressKeyAsync(string key)
    {
        return Task.Run(() =>
        {
            var keyCode = ParseKeyCode(key);
            if (keyCode == KeyCode.VcUndefined)
            {
                _logger?.LogWarning("Unknown key: {Key}", key);
                return;
            }

            _simulator.SimulateKeyPress(keyCode);
            _simulator.SimulateKeyRelease(keyCode);
            _logger?.LogDebug("Pressed key: {Key}", key);
        });
    }

    /// <inheritdoc />
    public Task PressHotkeyAsync(params string[] keys)
    {
        return Task.Run(() =>
        {
            var keyCodes = keys.Select(ParseKeyCode).Where(k => k != KeyCode.VcUndefined).ToArray();
            if (keyCodes.Length == 0)
            {
                _logger?.LogWarning("No valid keys in hotkey: {Keys}", string.Join("+", keys));
                return;
            }

            foreach (var keyCode in keyCodes)
            {
                _simulator.SimulateKeyPress(keyCode);
            }

            Task.Delay(50).Wait();

            foreach (var keyCode in keyCodes.Reverse())
            {
                _simulator.SimulateKeyRelease(keyCode);
            }

            _logger?.LogDebug("Pressed hotkey: {Keys}", string.Join("+", keys));
        });
    }

    /// <inheritdoc />
    public Task ScrollAsync(int deltaX, int deltaY)
    {
        return Task.Run(() =>
        {
            if (deltaY != 0)
            {
                _simulator.SimulateMouseWheel((short)(-deltaY / 120));
            }
            _logger?.LogDebug("Scrolled ({DeltaX}, {DeltaY})", deltaX, deltaY);
        });
    }

    /// <inheritdoc />
    public (int X, int Y) GetCursorPosition()
    {
        if (OperatingSystem.IsMacOS())
        {
            return GetMacCursorPosition();
        }
        
        if (OperatingSystem.IsWindows())
        {
            return GetWindowsCursorPosition();
        }
        
        if (OperatingSystem.IsLinux())
        {
            return GetLinuxCursorPosition();
        }

        return (0, 0);
    }

    /// <inheritdoc />
    public (int Width, int Height) GetScreenSize()
    {
        if (OperatingSystem.IsMacOS())
        {
            return GetMacScreenSize();
        }
        
        if (OperatingSystem.IsWindows())
        {
            return GetWindowsScreenSize();
        }
        
        if (OperatingSystem.IsLinux())
        {
            return GetLinuxScreenSize();
        }

        return (1920, 1080);
    }

#if NET10_0_OSX
    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreate(int dummy);

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventGetLocation(IntPtr event_, out CoreGraphics.CGPoint point);

    private static (int X, int Y) GetMacCursorPosition()
    {
        var eventRef = CGEventCreate(0);
        CGEventGetLocation(eventRef, out var point);
        return ((int)point.X, (int)point.Y);
    }

    private static (int Width, int Height) GetMacScreenSize()
    {
        var screen = CoreGraphics.CGScreen.Main;
        return ((int)screen.Width, (int)screen.Height);
    }
#else
    private static (int X, int Y) GetMacCursorPosition() => (0, 0);
    private static (int Width, int Height) GetMacScreenSize() => (1920, 1080);
#endif

#if NET10_0_OSX || NET10_0
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private static (int X, int Y) GetWindowsCursorPosition()
    {
        if (GetCursorPos(out var point))
        {
            return (point.X, point.Y);
        }
        return (0, 0);
    }

    private static (int Width, int Height) GetWindowsScreenSize()
    {
        return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }
#else
    private static (int X, int Y) GetWindowsCursorPosition() => (0, 0);
    private static (int Width, int Height) GetWindowsScreenSize() => (1920, 1080);
#endif

    [System.Runtime.InteropServices.DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [System.Runtime.InteropServices.DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [System.Runtime.InteropServices.DllImport("libX11.so.6")]
    private static extern IntPtr XQueryPointer(
        IntPtr display, IntPtr window,
        out IntPtr root, out IntPtr child,
        out int rootX, out int rootY,
        out int winX, out int winY,
        out uint mask);

    [System.Runtime.InteropServices.DllImport("libX11.so.6")]
    private static extern IntPtr XRootWindow(IntPtr display, int screen);

    [System.Runtime.InteropServices.DllImport("libX11.so.6")]
    private static extern int XGetGeometry(
        IntPtr display, IntPtr window,
        out IntPtr root, out int x, out int y,
        out uint width, out uint height,
        out uint borderWidth, out uint depth);

    private static (int X, int Y) GetLinuxCursorPosition()
    {
        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            return (0, 0);

        try
        {
            var root = XRootWindow(display, 0);
            XQueryPointer(display, root, out _, out _, out var rootX, out var rootY, out _, out _, out _);
            return (rootX, rootY);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static (int Width, int Height) GetLinuxScreenSize()
    {
        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            return (1920, 1080);

        try
        {
            var root = XRootWindow(display, 0);
            XGetGeometry(display, root, out _, out _, out _, out var width, out var height, out _, out _);
            return ((int)width, (int)height);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static MouseButton ConvertMouseButton(RpaMouseButton button) => button switch
    {
        RpaMouseButton.Left => MouseButton.Button1,
        RpaMouseButton.Middle => MouseButton.Button2,
        RpaMouseButton.Right => MouseButton.Button3,
        _ => MouseButton.Button1
    };

    private static KeyCode ParseKeyCode(string key)
    {
        var upperKey = key.ToUpperInvariant().Replace(" ", "");

        return upperKey switch
        {
            "ENTER" or "RETURN" => KeyCode.VcEnter,
            "ESCAPE" or "ESC" => KeyCode.VcEscape,
            "TAB" => KeyCode.VcTab,
            "BACKSPACE" => KeyCode.VcBackspace,
            "DELETE" => KeyCode.VcDelete,
            "INSERT" => KeyCode.VcInsert,
            "HOME" => KeyCode.VcHome,
            "END" => KeyCode.VcEnd,
            "PAGEUP" => KeyCode.VcPageUp,
            "PAGEDOWN" => KeyCode.VcPageDown,
            "UP" or "UPARROW" => KeyCode.VcUp,
            "DOWN" or "DOWNARROW" => KeyCode.VcDown,
            "LEFT" or "LEFTARROW" => KeyCode.VcLeft,
            "RIGHT" or "RIGHTARROW" => KeyCode.VcRight,
            "F1" => KeyCode.VcF1,
            "F2" => KeyCode.VcF2,
            "F3" => KeyCode.VcF3,
            "F4" => KeyCode.VcF4,
            "F5" => KeyCode.VcF5,
            "F6" => KeyCode.VcF6,
            "F7" => KeyCode.VcF7,
            "F8" => KeyCode.VcF8,
            "F9" => KeyCode.VcF9,
            "F10" => KeyCode.VcF10,
            "F11" => KeyCode.VcF11,
            "F12" => KeyCode.VcF12,
            "CTRL" or "CONTROL" => KeyCode.VcLeftControl,
            "ALT" => KeyCode.VcLeftAlt,
            "SHIFT" => KeyCode.VcLeftShift,
            "WIN" or "WINDOWS" or "META" => KeyCode.VcLeftMeta,
            "A" => KeyCode.VcA,
            "B" => KeyCode.VcB,
            "C" => KeyCode.VcC,
            "D" => KeyCode.VcD,
            "E" => KeyCode.VcE,
            "F" => KeyCode.VcF,
            "G" => KeyCode.VcG,
            "H" => KeyCode.VcH,
            "I" => KeyCode.VcI,
            "J" => KeyCode.VcJ,
            "K" => KeyCode.VcK,
            "L" => KeyCode.VcL,
            "M" => KeyCode.VcM,
            "N" => KeyCode.VcN,
            "O" => KeyCode.VcO,
            "P" => KeyCode.VcP,
            "Q" => KeyCode.VcQ,
            "R" => KeyCode.VcR,
            "S" => KeyCode.VcS,
            "T" => KeyCode.VcT,
            "U" => KeyCode.VcU,
            "V" => KeyCode.VcV,
            "W" => KeyCode.VcW,
            "X" => KeyCode.VcX,
            "Y" => KeyCode.VcY,
            "Z" => KeyCode.VcZ,
            "0" => KeyCode.Vc0,
            "1" => KeyCode.Vc1,
            "2" => KeyCode.Vc2,
            "3" => KeyCode.Vc3,
            "4" => KeyCode.Vc4,
            "5" => KeyCode.Vc5,
            "6" => KeyCode.Vc6,
            "7" => KeyCode.Vc7,
            "8" => KeyCode.Vc8,
            "9" => KeyCode.Vc9,
            "SPACE" or " " => KeyCode.VcSpace,
            _ => KeyCode.VcUndefined
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
