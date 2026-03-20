using Microsoft.Extensions.Logging;
using SkiaSharp;
using Moq;
using NanoBot.Core.Configuration;
using NanoBot.Core.Tools.Rpa;
using NanoBot.Infrastructure.Tools.Rpa;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Tools.Rpa;

public class RpaServiceTests
{
    private readonly Mock<IInputSimulator> _inputSimulatorMock;
    private readonly Mock<IScreenCapture> _screenCaptureMock;
    private readonly Mock<IOmniParserClient> _omniParserClientMock;
    private readonly ImageOptimizer _imageOptimizer;
    private readonly ILogger<RpaService> _logger;
    private readonly RpaToolsConfig _config;
    private readonly byte[] _fakeImageBytes;

    public RpaServiceTests()
    {
        _inputSimulatorMock = new Mock<IInputSimulator>();
        _screenCaptureMock = new Mock<IScreenCapture>();
        _omniParserClientMock = new Mock<IOmniParserClient>();
        _imageOptimizer = new ImageOptimizer();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _logger = loggerFactory.CreateLogger<RpaService>();
        _config = new RpaToolsConfig { ScreenshotPath = null };

        // Generate a valid 10x10 red PNG using SkiaSharp
        _fakeImageBytes = GeneratePngImage(10, 10, SKColors.Red);
    }

    private static byte[] GeneratePngImage(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private RpaService CreateService(
        Mock<IInputSimulator>? inputSimulator = null,
        Mock<IScreenCapture>? screenCapture = null,
        Mock<IOmniParserClient>? omniParserClient = null,
        ImageOptimizer? imageOptimizer = null,
        RpaToolsConfig? config = null)
    {
        return new RpaService(
            inputSimulator?.Object ?? _inputSimulatorMock.Object,
            screenCapture?.Object ?? _screenCaptureMock.Object,
            omniParserClient?.Object ?? _omniParserClientMock.Object,
            imageOptimizer ?? _imageOptimizer,
            _logger,
            config ?? _config);
    }

    [Fact]
    public async Task ExecuteFlowAsync_EmptyFlow_Succeeds()
    {
        var service = CreateService();
        var request = new RpaFlowRequest { Flows = [] };

        var result = await service.ExecuteFlowAsync(request);

        Assert.True(result.Success);
        Assert.Equal(0, result.CompletedSteps);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteFlowAsync_SingleMove_CallsInputSimulator()
    {
        var inputSim = new Mock<IInputSimulator>();
        var service = CreateService(inputSimulator: inputSim);

        var request = new RpaFlowRequest
        {
            Flows =
            [
                new RpaMoveAction { Type = RpaActionType.Move, X = 100, Y = 200 }
            ]
        };

        await service.ExecuteFlowAsync(request);

        inputSim.Verify(x => x.MoveMouseAsync(100, 200, null), Times.Once);
    }

    [Fact]
    public async Task ExecuteFlowAsync_MoveClickType_ExecutesInOrder()
    {
        var inputSim = new Mock<IInputSimulator>();
        var service = CreateService(inputSimulator: inputSim);
        var callOrder = new List<string>();

        inputSim.Setup(x => x.MoveMouseAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()))
            .Callback(() => callOrder.Add("move"));
        inputSim.Setup(x => x.ClickAsync(It.IsAny<RpaMouseButton>()))
            .Callback(() => callOrder.Add("click"));
        inputSim.Setup(x => x.TypeTextAsync(It.IsAny<string>()))
            .Callback(() => callOrder.Add("type"));

        var request = new RpaFlowRequest
        {
            Flows =
            [
                new RpaMoveAction { Type = RpaActionType.Move, X = 10, Y = 20 },
                new RpaClickAction { Type = RpaActionType.Click },
                new RpaTypeAction { Type = RpaActionType.Type, Text = "hello" }
            ]
        };

        var result = await service.ExecuteFlowAsync(request);

        Assert.True(result.Success);
        Assert.Equal(3, result.CompletedSteps);
        Assert.Equal(new[] { "move", "click", "type" }, callOrder);
        inputSim.Verify(x => x.TypeTextAsync("hello"), Times.Once);
    }

    [Fact]
    public async Task ExecuteFlowAsync_StepThrowsException_StopsAndReturnsError()
    {
        var inputSim = new Mock<IInputSimulator>();
        inputSim.Setup(x => x.MoveMouseAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()))
            .Throws(new InvalidOperationException("simulator error"));

        var service = CreateService(inputSimulator: inputSim);

        var request = new RpaFlowRequest
        {
            Flows =
            [
                new RpaMoveAction { Type = RpaActionType.Move, X = 1, Y = 2 },
                new RpaClickAction { Type = RpaActionType.Click }
            ]
        };

        var result = await service.ExecuteFlowAsync(request);

        Assert.False(result.Success);
        Assert.Equal(0, result.CompletedSteps);
        Assert.Contains("Step 1 failed", result.Error);
        inputSim.Verify(x => x.ClickAsync(It.IsAny<RpaMouseButton>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WithDelayAfterMs_CompletesAllSteps()
    {
        var inputSim = new Mock<IInputSimulator>();
        var service = CreateService(inputSimulator: inputSim);

        var request = new RpaFlowRequest
        {
            Flows =
            [
                new RpaMoveAction { Type = RpaActionType.Move, X = 0, Y = 0, DelayAfterMs = 10 },
                new RpaClickAction { Type = RpaActionType.Click, DelayAfterMs = 10 }
            ]
        };

        var result = await service.ExecuteFlowAsync(request);

        Assert.True(result.Success);
        Assert.Equal(2, result.CompletedSteps);
    }

    [Fact]
    public async Task ExecuteFlowAsync_RpaTypeAction_VerifiesTextParameter()
    {
        var inputSim = new Mock<IInputSimulator>();
        var service = CreateService(inputSimulator: inputSim);

        var request = new RpaFlowRequest
        {
            Flows = [new RpaTypeAction { Type = RpaActionType.Type, Text = "Hello World!" }]
        };

        await service.ExecuteFlowAsync(request);

        inputSim.Verify(x => x.TypeTextAsync("Hello World!"), Times.Once);
    }

    [Fact]
    public async Task ExecuteFlowAsync_RpaPressAction_VerifiesKeyParameter()
    {
        var inputSim = new Mock<IInputSimulator>();
        var service = CreateService(inputSimulator: inputSim);

        var request = new RpaFlowRequest
        {
            Flows = [new RpaPressAction { Type = RpaActionType.Press, Key = "Enter" }]
        };

        await service.ExecuteFlowAsync(request);

        inputSim.Verify(x => x.PressKeyAsync("Enter"), Times.Once);
    }

    [Fact]
    public async Task ExecuteFlowAsync_RpaHotkeyAction_VerifiesKeysParameter()
    {
        var inputSim = new Mock<IInputSimulator>();
        var service = CreateService(inputSimulator: inputSim);

        var request = new RpaFlowRequest
        {
            Flows = [new RpaHotkeyAction { Type = RpaActionType.Hotkey, Keys = ["Ctrl", "C"] }]
        };

        await service.ExecuteFlowAsync(request);

        inputSim.Verify(x => x.PressHotkeyAsync("Ctrl", "C"), Times.Once);
    }

    [Fact]
    public async Task ExecuteFlowAsync_RpaScrollAction_VerifiesDeltaParameters()
    {
        var inputSim = new Mock<IInputSimulator>();
        var service = CreateService(inputSimulator: inputSim);

        var request = new RpaFlowRequest
        {
            Flows = [new RpaScrollAction { Type = RpaActionType.Scroll, DeltaX = 0, DeltaY = -120 }]
        };

        await service.ExecuteFlowAsync(request);

        inputSim.Verify(x => x.ScrollAsync(0, -120), Times.Once);
    }

    [Fact]
    public async Task ExecuteFlowAsync_RpaScreenshotAction_WithoutVision_DoesNotCallOmniParser()
    {
        var screenCapture = new Mock<IScreenCapture>();
        screenCapture.Setup(x => x.CapturePrimaryScreenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_fakeImageBytes);
        var imageOptimizer = new ImageOptimizer();
        var service = CreateService(screenCapture: screenCapture, imageOptimizer: imageOptimizer);
        _omniParserClientMock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new RpaFlowRequest
        {
            Flows = [new RpaScreenshotAction { Type = RpaActionType.Screenshot, OutputRef = "test" }],
            EnableVision = false
        };

        await service.ExecuteFlowAsync(request);

        _omniParserClientMock.Verify(x => x.ParseAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteFlowAsync_RpaScreenshotAction_WithVision_CallsOmniParser()
    {
        var screenCapture = new Mock<IScreenCapture>();
        screenCapture.Setup(x => x.CapturePrimaryScreenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_fakeImageBytes);

        var omniParser = new Mock<IOmniParserClient>();
        omniParser.Setup(x => x.ParseAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OmniParserResult { ParsedContent = [] });

        var imageOptimizer = new ImageOptimizer();
        var service = CreateService(screenCapture: screenCapture, omniParserClient: omniParser, imageOptimizer: imageOptimizer);

        var request = new RpaFlowRequest
        {
            Flows = [new RpaScreenshotAction { Type = RpaActionType.Screenshot, OutputRef = "test" }],
            EnableVision = true
        };

        var result = await service.ExecuteFlowAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.VisionResults);
        Assert.Contains("test", result.VisionResults.Keys);
        omniParser.Verify(x => x.ParseAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteFlowAsync_RpaClickAction_WithCoordinates_MovesBeforeClicking()
    {
        var inputSim = new Mock<IInputSimulator>();
        var service = CreateService(inputSimulator: inputSim);

        var request = new RpaFlowRequest
        {
            Flows = [new RpaClickAction { Type = RpaActionType.Click, X = 300, Y = 400 }]
        };

        await service.ExecuteFlowAsync(request);

        inputSim.Verify(x => x.MoveMouseAsync(300, 400), Times.Once);
        inputSim.Verify(x => x.ClickAsync(RpaMouseButton.Left), Times.Once);
    }

    [Fact]
    public async Task ExecuteFlowAsync_RpaDoubleClickAction_MovesThenDoubleClicks()
    {
        var inputSim = new Mock<IInputSimulator>();
        var service = CreateService(inputSimulator: inputSim);

        var request = new RpaFlowRequest
        {
            Flows = [new RpaDoubleClickAction { Type = RpaActionType.DoubleClick, X = 50, Y = 60 }]
        };

        await service.ExecuteFlowAsync(request);

        inputSim.Verify(x => x.MoveMouseAsync(50, 60), Times.Once);
        inputSim.Verify(x => x.DoubleClickAsync(RpaMouseButton.Left), Times.Once);
    }

    [Fact]
    public async Task ExecuteFlowAsync_RpaRightClickAction_MovesThenRightClicks()
    {
        var inputSim = new Mock<IInputSimulator>();
        var service = CreateService(inputSimulator: inputSim);

        var request = new RpaFlowRequest
        {
            Flows = [new RpaRightClickAction { Type = RpaActionType.RightClick, X = 10, Y = 20 }]
        };

        await service.ExecuteFlowAsync(request);

        inputSim.Verify(x => x.MoveMouseAsync(10, 20), Times.Once);
        inputSim.Verify(x => x.RightClickAsync(), Times.Once);
    }

    [Fact]
    public async Task ExecuteFlowAsync_RpaDragAction_CallsDragSimulator()
    {
        var inputSim = new Mock<IInputSimulator>();
        var service = CreateService(inputSimulator: inputSim);

        var request = new RpaFlowRequest
        {
            Flows = [new RpaDragAction { Type = RpaActionType.Drag, FromX = 100, FromY = 200, ToX = 300, ToY = 400, DurationMs = 500 }]
        };

        await service.ExecuteFlowAsync(request);

        inputSim.Verify(x => x.DragAsync(100, 200, 300, 400, 500), Times.Once);
    }

    [Fact]
    public async Task ExecuteFlowAsync_RpaWaitAction_DelaysCorrectly()
    {
        var service = CreateService();

        var request = new RpaFlowRequest
        {
            Flows = [new RpaWaitAction { Type = RpaActionType.Wait, DurationMs = 50 }]
        };

        var result = await service.ExecuteFlowAsync(request);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteActionAsync_Success_DoesNotThrow()
    {
        var service = CreateService();

        var exception = await Record.ExceptionAsync(() =>
            service.ExecuteActionAsync(new RpaMoveAction { Type = RpaActionType.Move, X = 0, Y = 0 }));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ExecuteActionAsync_Failure_ThrowsInvalidOperationException()
    {
        var inputSim = new Mock<IInputSimulator>();
        inputSim.Setup(x => x.MoveMouseAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()))
            .Throws(new InvalidOperationException("simulator error"));

        var service = CreateService(inputSimulator: inputSim);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteActionAsync(new RpaMoveAction { Type = RpaActionType.Move, X = 0, Y = 0 }));
    }

    [Fact]
    public async Task GetScreenSizeAsync_DelegatesToInputSimulator()
    {
        var inputSim = new Mock<IInputSimulator>();
        inputSim.Setup(x => x.GetScreenSize()).Returns((1920, 1080));

        var service = CreateService(inputSimulator: inputSim);

        var (width, height) = await service.GetScreenSizeAsync();

        Assert.Equal(1920, width);
        Assert.Equal(1080, height);
    }

    [Fact]
    public async Task GetCursorPositionAsync_DelegatesToInputSimulator()
    {
        var inputSim = new Mock<IInputSimulator>();
        inputSim.Setup(x => x.GetCursorPosition()).Returns((123, 456));

        var service = CreateService(inputSimulator: inputSim);

        var (x, y) = await service.GetCursorPositionAsync();

        Assert.Equal(123, x);
        Assert.Equal(456, y);
    }

    [Fact]
    public async Task GetHealthStatusAsync_SharpHookAvailable_ReturnsAvailable()
    {
        var inputSim = new Mock<IInputSimulator>();
        inputSim.Setup(x => x.GetScreenSize()).Returns((2560, 1440));

        var service = CreateService(inputSimulator: inputSim);

        var status = await service.GetHealthStatusAsync();

        Assert.True(status.SharpHookAvailable);
        Assert.Equal((2560, 1440), status.ScreenSize);
        Assert.Null(status.SharpHookError);
    }

    [Fact]
    public async Task GetHealthStatusAsync_SharpHookThrows_ReturnsUnavailable()
    {
        var inputSim = new Mock<IInputSimulator>();
        inputSim.Setup(x => x.GetScreenSize())
            .Throws(new Exception("access denied"));

        var service = CreateService(inputSimulator: inputSim);

        var status = await service.GetHealthStatusAsync();

        Assert.False(status.SharpHookAvailable);
        Assert.NotNull(status.SharpHookError);
    }

    [Fact]
    public async Task GetHealthStatusAsync_OmniParserAvailable_ReturnsAvailable()
    {
        var inputSim = new Mock<IInputSimulator>();
        inputSim.Setup(x => x.GetScreenSize()).Returns((1920, 1080));

        var omniParser = new Mock<IOmniParserClient>();
        omniParser.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService(inputSimulator: inputSim, omniParserClient: omniParser);

        var status = await service.GetHealthStatusAsync();

        Assert.True(status.OmniParserAvailable);
        Assert.Null(status.OmniParserError);
    }

    [Fact]
    public async Task GetHealthStatusAsync_OmniParserUnavailable_ReturnsUnavailable()
    {
        var inputSim = new Mock<IInputSimulator>();
        inputSim.Setup(x => x.GetScreenSize()).Returns((1920, 1080));

        var omniParser = new Mock<IOmniParserClient>();
        omniParser.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService(inputSimulator: inputSim, omniParserClient: omniParser);

        var status = await service.GetHealthStatusAsync();

        Assert.False(status.OmniParserAvailable);
    }

    [Fact]
    public async Task GetHealthStatusAsync_OmniParserThrows_ReturnsError()
    {
        var inputSim = new Mock<IInputSimulator>();
        inputSim.Setup(x => x.GetScreenSize()).Returns((1920, 1080));

        var omniParser = new Mock<IOmniParserClient>();
        omniParser.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .Throws(new Exception("connection refused"));

        var service = CreateService(inputSimulator: inputSim, omniParserClient: omniParser);

        var status = await service.GetHealthStatusAsync();

        Assert.False(status.OmniParserAvailable);
        Assert.NotNull(status.OmniParserError);
    }

    [Fact]
    public async Task StartAsync_WithAutoStartAndOmniParserClient_StartsOmniParser()
    {
        var omniParser = new Mock<IOmniParserClient>();
        var config = new RpaToolsConfig { AutoStartService = true };
        var service = CreateService(omniParserClient: omniParser, config: config);

        await service.StartAsync();

        omniParser.Verify(x => x.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_StopsOmniParser()
    {
        var omniParser = new Mock<IOmniParserClient>();
        var service = CreateService(omniParserClient: omniParser);

        await service.StopAsync();

        omniParser.Verify(x => x.StopAsync(), Times.Once);
    }

    [Fact]
    public async Task AnalyzeScreenAsync_WithOmniParserClient_ReturnsParsedResult()
    {
        var screenCapture = new Mock<IScreenCapture>();
        screenCapture.Setup(x => x.CapturePrimaryScreenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_fakeImageBytes);

        var omniParser = new Mock<IOmniParserClient>();
        var expectedResult = new OmniParserResult
        {
            ParsedContent = new List<OmniParserElement>
            {
                new OmniParserElement { Bbox = [10, 20, 30, 40], Label = "button", Type = "button", Confidence = 0.95 }
            }
        };
        omniParser.Setup(x => x.ParseAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var imageOptimizer = new ImageOptimizer();
        var service = CreateService(screenCapture: screenCapture, omniParserClient: omniParser, imageOptimizer: imageOptimizer);

        var result = await service.AnalyzeScreenAsync();

        Assert.NotNull(result);
        Assert.Single(result.ParsedContent);
        Assert.Equal("button", result.ParsedContent[0].Label);
    }

    [Fact]
    public async Task AnalyzeScreenAsync_WithoutOmniParserClient_ThrowsInvalidOperationException()
    {
        var service = CreateService(omniParserClient: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeScreenAsync());
    }
}
