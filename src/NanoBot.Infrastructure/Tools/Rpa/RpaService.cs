using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Core.Tools.Rpa;

namespace NanoBot.Infrastructure.Tools.Rpa;

/// <summary>
/// RPA 服务实现
/// </summary>
public class RpaService : IRpaService
{
    private readonly IInputSimulator _inputSimulator;
    private readonly IScreenCapture _screenCapture;
    private readonly IOmniParserClient? _omniParserClient;
    private readonly ImageOptimizer _imageOptimizer;
    private readonly ILogger<RpaService>? _logger;
    private readonly RpaToolsConfig _config;

    public RpaService(
        IInputSimulator inputSimulator,
        IScreenCapture screenCapture,
        IOmniParserClient? omniParserClient,
        ImageOptimizer imageOptimizer,
        ILogger<RpaService>? logger,
        RpaToolsConfig config)
    {
        _inputSimulator = inputSimulator;
        _screenCapture = screenCapture;
        _omniParserClient = omniParserClient;
        _imageOptimizer = imageOptimizer;
        _logger = logger;
        _config = config;
    }

    /// <inheritdoc />
    public async Task<RpaFlowResult> ExecuteFlowAsync(RpaFlowRequest request, CancellationToken ct = default)
    {
        var result = new RpaFlowResult
        {
            VisionResults = new Dictionary<string, OmniParserResult>()
        };

        try
        {
            var completedSteps = 0;
            string? currentError = null;

            for (int i = 0; i < request.Flows.Length; i++)
            {
                var action = request.Flows[i];
                try
                {
                    await ExecuteActionInternalAsync(action, request, result.VisionResults, ct);
                    completedSteps++;

                    if (action.DelayAfterMs.HasValue && action.DelayAfterMs > 0)
                    {
                        await Task.Delay(action.DelayAfterMs.Value, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    currentError = $"Step {i + 1} failed: {ex.Message}";
                    _logger?.LogWarning(ex, "RPA flow step {Step} failed: {Message}", i + 1, ex.Message);
                    break;
                }
            }

            result = result with
            {
                Success = completedSteps == request.Flows.Length,
                CompletedSteps = completedSteps,
                Error = currentError
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = result with
            {
                Success = false,
                Error = ex.Message
            };
        }

        return result;
    }

    private async Task ExecuteActionInternalAsync(
        RpaAction action,
        RpaFlowRequest request,
        Dictionary<string, OmniParserResult> visionResults,
        CancellationToken ct)
    {
        switch (action)
        {
            case RpaMoveAction move:
                var (x, y) = ResolveCoordinates(move.X, move.Y, visionResults);
                await _inputSimulator.MoveMouseAsync(x, y, move.DurationMs);
                break;

            case RpaClickAction click:
                await ExecuteClickAsync(click, visionResults);
                break;

            case RpaDoubleClickAction doubleClick:
                await ExecuteDoubleClickAsync(doubleClick, visionResults);
                break;

            case RpaRightClickAction rightClick:
                await ExecuteRightClickAsync(rightClick, visionResults);
                break;

            case RpaDragAction drag:
                var (fromX, fromY) = ResolveCoordinates(drag.FromX, drag.FromY, visionResults);
                var (toX, toY) = ResolveCoordinates(drag.ToX, drag.ToY, visionResults);
                await _inputSimulator.DragAsync(fromX, fromY, toX, toY, drag.DurationMs);
                break;

            case RpaTypeAction type:
                await _inputSimulator.TypeTextAsync(type.Text);
                break;

            case RpaPressAction press:
                await _inputSimulator.PressKeyAsync(press.Key);
                break;

            case RpaHotkeyAction hotkey:
                await _inputSimulator.PressHotkeyAsync(hotkey.Keys);
                break;

            case RpaWaitAction wait:
                await Task.Delay(wait.DurationMs, ct);
                break;

            case RpaScreenshotAction screenshot:
                await ExecuteScreenshotAsync(screenshot, request.EnableVision, visionResults, ct);
                break;

            case RpaScrollAction scroll:
                await _inputSimulator.ScrollAsync(scroll.DeltaX, scroll.DeltaY);
                break;

            default:
                _logger?.LogWarning("Unknown action type: {Type}", action.Type);
                break;
        }
    }

    private async Task ExecuteClickAsync(RpaClickAction click, Dictionary<string, OmniParserResult> visionResults)
    {
        if (click.X.HasValue && click.Y.HasValue)
        {
            var (x, y) = ResolveCoordinates(click.X.Value, click.Y.Value, visionResults);
            await _inputSimulator.MoveMouseAsync(x, y);
        }
        await _inputSimulator.ClickAsync(click.Button);
    }

    private async Task ExecuteDoubleClickAsync(RpaDoubleClickAction doubleClick, Dictionary<string, OmniParserResult> visionResults)
    {
        if (doubleClick.X.HasValue && doubleClick.Y.HasValue)
        {
            var (x, y) = ResolveCoordinates(doubleClick.X.Value, doubleClick.Y.Value, visionResults);
            await _inputSimulator.MoveMouseAsync(x, y);
        }
        await _inputSimulator.DoubleClickAsync(doubleClick.Button);
    }

    private async Task ExecuteRightClickAsync(RpaRightClickAction rightClick, Dictionary<string, OmniParserResult> visionResults)
    {
        if (rightClick.X.HasValue && rightClick.Y.HasValue)
        {
            var (x, y) = ResolveCoordinates(rightClick.X.Value, rightClick.Y.Value, visionResults);
            await _inputSimulator.MoveMouseAsync(x, y);
        }
        await _inputSimulator.RightClickAsync();
    }

    private async Task ExecuteScreenshotAsync(
        RpaScreenshotAction screenshot,
        bool enableVision,
        Dictionary<string, OmniParserResult> visionResults,
        CancellationToken ct)
    {
        var rawImage = await _screenCapture.CapturePrimaryScreenAsync(ct);
        var optimized = await _imageOptimizer.OptimizeForOmniParserAsync(rawImage, null, ct);

        if (!string.IsNullOrEmpty(_config.ScreenshotPath))
        {
            var debugPath = Path.Combine(_config.ScreenshotPath, $"screenshot_{screenshot.OutputRef}_{DateTime.UtcNow:yyyyMMddHHmmss}.jpg");
            await File.WriteAllBytesAsync(debugPath, optimized.Data, ct);
            _logger?.LogDebug("Screenshot saved to {Path}", debugPath);
        }

        if (enableVision && _omniParserClient != null)
        {
            try
            {
                var omniResult = await _omniParserClient.ParseAsync(optimized.Data, ct);
                visionResults[screenshot.OutputRef] = omniResult;
                _logger?.LogDebug("OmniParser found {Count} elements for {Ref}",
                    omniResult.ParsedContent.Count, screenshot.OutputRef);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "OmniParser analysis failed for {Ref}", screenshot.OutputRef);
            }
        }
    }

    private static (int X, int Y) ResolveCoordinates(int x, int y, Dictionary<string, OmniParserResult> visionResults)
    {
        var xStr = x.ToString();
        var yStr = y.ToString();

        var xResolved = ResolveCoordinateValue(xStr, visionResults);
        var yResolved = ResolveCoordinateValue(yStr, visionResults);

        return (xResolved, yResolved);
    }

    private static int ResolveCoordinateValue(string value, Dictionary<string, OmniParserResult> visionResults)
    {
        var match = Regex.Match(value, @"^\{\{vision\.(\w+)\[(\d+)\]\.bbox\[(\d+)\]\}\}$");
        if (!match.Success)
        {
            if (int.TryParse(value, out var direct))
                return direct;
            return 0;
        }

        var refName = match.Groups[1].Value;
        var elementIndex = int.Parse(match.Groups[2].Value);
        var bboxIndex = int.Parse(match.Groups[3].Value);

        if (!visionResults.TryGetValue(refName, out var result))
            return 0;

        if (elementIndex >= result.ParsedContent.Count)
            return 0;

        if (bboxIndex >= result.ParsedContent[elementIndex].Bbox.Length)
            return 0;

        return result.ParsedContent[elementIndex].Bbox[bboxIndex];
    }

    /// <inheritdoc />
    public async Task ExecuteActionAsync(RpaAction action, CancellationToken ct = default)
    {
        var request = new RpaFlowRequest { Flows = new[] { action } };
        var result = await ExecuteFlowAsync(request, ct);
        if (!result.Success && result.Error != null)
        {
            throw new InvalidOperationException(result.Error);
        }
    }

    /// <inheritdoc />
    public Task<(int Width, int Height)> GetScreenSizeAsync() =>
        Task.FromResult(_inputSimulator.GetScreenSize());

    /// <inheritdoc />
    public Task<(int X, int Y)> GetCursorPositionAsync() =>
        Task.FromResult(_inputSimulator.GetCursorPosition());

    /// <inheritdoc />
    public async Task<OmniParserResult> AnalyzeScreenAsync(CancellationToken ct = default)
    {
        if (_omniParserClient == null)
        {
            throw new InvalidOperationException("OmniParser is not available");
        }

        var rawImage = await _screenCapture.CapturePrimaryScreenAsync(ct);
        var optimized = await _imageOptimizer.OptimizeForOmniParserAsync(rawImage, null, ct);
        return await _omniParserClient.ParseAsync(optimized.Data, ct);
    }

    /// <inheritdoc />
    public async Task<RpaHealthStatus> GetHealthStatusAsync(CancellationToken ct = default)
    {
        var status = new RpaHealthStatus();

        try
        {
            var screenSize = await GetScreenSizeAsync();
            status = status with
            {
                SharpHookAvailable = true,
                ScreenSize = screenSize
            };
        }
        catch (Exception ex)
        {
            status = status with
            {
                SharpHookAvailable = false,
                SharpHookError = ex.Message
            };
        }

        if (_omniParserClient != null)
        {
            try
            {
                var available = await _omniParserClient.IsAvailableAsync(ct);
                status = status with { OmniParserAvailable = available };
            }
            catch (Exception ex)
            {
                status = status with
                {
                    OmniParserAvailable = false,
                    OmniParserError = ex.Message
                };
            }
        }

        return status;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_config.AutoStartService && _omniParserClient != null)
        {
            await _omniParserClient.StartAsync(ct);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (_omniParserClient != null)
        {
            await _omniParserClient.StopAsync();
        }
    }
}
