using System.Text.Json;
using Microsoft.Extensions.AI;
using Moq;
using NanoBot.Core.Tools.Rpa;
using NanoBot.Tools.BuiltIn.Rpa;
using Xunit;

namespace NanoBot.Tools.Tests;

public class RpaToolsTests
{
    private static AIFunctionArguments FlowsArgs(params RpaAction[] flows) =>
        new() { ["request"] = new RpaFlowRequest { Flows = flows, EnableVision = false, ScreenshotPath = null } };

    private static AIFunctionArguments FlowsArgsWithVision(params RpaAction[] flows) =>
        new() { ["request"] = new RpaFlowRequest { Flows = flows, EnableVision = true, ScreenshotPath = null } };

    private static AIFunctionArguments FlowsArgsCustom(bool enableVision, string? screenshotPath, params RpaAction[] flows) =>
        new() { ["request"] = new RpaFlowRequest { Flows = flows, EnableVision = enableVision, ScreenshotPath = screenshotPath } };

    [Fact]
    public void CreateRpaTool_ReturnsNonNull()
    {
        var mockService = new Mock<IRpaService>();
        var tool = RpaTools.CreateRpaTool(mockService.Object);

        Assert.NotNull(tool);
    }

    [Fact]
    public void CreateRpaTool_ReturnsAIFunction()
    {
        var mockService = new Mock<IRpaService>();
        var tool = RpaTools.CreateRpaTool(mockService.Object);

        Assert.IsAssignableFrom<AIFunction>(tool);
    }

    [Fact]
    public void CreateRpaTool_HasCorrectName()
    {
        var mockService = new Mock<IRpaService>();
        var tool = RpaTools.CreateRpaTool(mockService.Object);

        Assert.Equal("rpa", tool.Name);
    }

    [Fact]
    public void CreateRpaTool_Description_ContainsRPAAndFlows()
    {
        var mockService = new Mock<IRpaService>();
        var tool = RpaTools.CreateRpaTool(mockService.Object);

        var description = tool.Description;
        Assert.Contains("RPA", description);
        Assert.Contains("flows", description);
    }

    [Fact]
    public void CreateRpaTool_Description_ContainsSupportedOperations()
    {
        var mockService = new Mock<IRpaService>();
        var tool = RpaTools.CreateRpaTool(mockService.Object);

        var description = tool.Description;
        Assert.Contains("mouse", description.ToLower());
        Assert.Contains("click", description.ToLower());
        Assert.Contains("type", description.ToLower());
    }

    [Fact]
    public async Task ExecuteRpaTool_SuccessfulFlow_ReturnsSuccessResponse()
    {
        var mockService = new Mock<IRpaService>();
        mockService.Setup(x => x.ExecuteFlowAsync(It.IsAny<RpaFlowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RpaFlowResult { Success = true, CompletedSteps = 2, Error = null });

        var tool = RpaTools.CreateRpaTool(mockService.Object);
        var func = (AIFunction)tool;

        var args = FlowsArgs(
            new RpaMoveAction { Type = RpaActionType.Move, X = 100, Y = 200 },
            new RpaClickAction { Type = RpaActionType.Click }
        );

        var result = await func.InvokeAsync(args, CancellationToken.None);

        var resultText = result?.ToString() ?? string.Empty;
        Assert.Contains("\"success\":true", resultText);
        Assert.Contains("\"completed_steps\":2", resultText);
        Assert.Contains("\"total_steps\":2", resultText);
        mockService.Verify(x => x.ExecuteFlowAsync(It.IsAny<RpaFlowRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteRpaTool_PartialFailure_ReturnsFailureWithError()
    {
        var mockService = new Mock<IRpaService>();
        mockService.Setup(x => x.ExecuteFlowAsync(It.IsAny<RpaFlowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RpaFlowResult { Success = false, CompletedSteps = 1, Error = "Step 2 failed: element not found" });

        var tool = RpaTools.CreateRpaTool(mockService.Object);
        var func = (AIFunction)tool;

        var args = FlowsArgs(
            new RpaMoveAction { Type = RpaActionType.Move, X = 0, Y = 0 },
            new RpaClickAction { Type = RpaActionType.Click }
        );

        var result = await func.InvokeAsync(args, CancellationToken.None);

        var resultText = result?.ToString() ?? string.Empty;
        Assert.Contains("\"success\":false", resultText);
        Assert.Contains("\"completed_steps\":1", resultText);
        Assert.Contains("element not found", resultText);
    }

    [Fact]
    public async Task ExecuteRpaTool_WithVisionResults_IncludesVisionSummary()
    {
        var mockService = new Mock<IRpaService>();
        mockService.Setup(x => x.ExecuteFlowAsync(It.IsAny<RpaFlowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RpaFlowResult
            {
                Success = true,
                CompletedSteps = 1,
                VisionResults = new Dictionary<string, OmniParserResult>
                {
                    ["desktop"] = new OmniParserResult
                    {
                        ParsedContent = new List<OmniParserElement>
                        {
                            new OmniParserElement
                            {
                                Bbox = [10, 20, 100, 50],
                                Label = "search input field",
                                Type = "input",
                                Confidence = 0.95
                            }
                        }
                    }
                }
            });

        var tool = RpaTools.CreateRpaTool(mockService.Object);
        var func = (AIFunction)tool;

        var args = FlowsArgsWithVision(
            new RpaScreenshotAction { Type = RpaActionType.Screenshot, OutputRef = "desktop" }
        );

        var result = await func.InvokeAsync(args, CancellationToken.None);

        var resultText = result?.ToString() ?? string.Empty;
        Assert.Contains("vision_summary", resultText);
        Assert.Contains("desktop", resultText);
        Assert.Contains("search input field", resultText);
        Assert.Contains("1 elements", resultText);
    }

    [Fact]
    public async Task ExecuteRpaTool_WithoutVisionResults_OmitsVisionSummary()
    {
        var mockService = new Mock<IRpaService>();
        mockService.Setup(x => x.ExecuteFlowAsync(It.IsAny<RpaFlowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RpaFlowResult { Success = true, CompletedSteps = 2, VisionResults = null });

        var tool = RpaTools.CreateRpaTool(mockService.Object);
        var func = (AIFunction)tool;

        var args = FlowsArgs(
            new RpaMoveAction { Type = RpaActionType.Move, X = 10, Y = 20 },
            new RpaClickAction { Type = RpaActionType.Click }
        );

        var result = await func.InvokeAsync(args, CancellationToken.None);

        var resultText = result?.ToString() ?? string.Empty;
        Assert.DoesNotContain("\"vision_summary\":\"", resultText);
    }

    [Fact]
    public async Task ExecuteRpaTool_PassesFlowsToService()
    {
        RpaFlowRequest? capturedRequest = null;
        var mockService = new Mock<IRpaService>();
        mockService.Setup(x => x.ExecuteFlowAsync(It.IsAny<RpaFlowRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RpaFlowRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new RpaFlowResult { Success = true, CompletedSteps = 1 });

        var tool = RpaTools.CreateRpaTool(mockService.Object);
        var func = (AIFunction)tool;

        var args = FlowsArgsCustom(true, "/tmp/screenshots",
            new RpaTypeAction { Type = RpaActionType.Type, Text = "hello" }
        );

        await func.InvokeAsync(args, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest.EnableVision);
        Assert.Equal("/tmp/screenshots", capturedRequest.ScreenshotPath);
    }

    [Fact]
    public async Task ExecuteRpaTool_PassesEnableVisionFlag()
    {
        RpaFlowRequest? capturedRequest = null;
        var mockService = new Mock<IRpaService>();
        mockService.Setup(x => x.ExecuteFlowAsync(It.IsAny<RpaFlowRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RpaFlowRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new RpaFlowResult { Success = true, CompletedSteps = 0 });

        var tool = RpaTools.CreateRpaTool(mockService.Object);
        var func = (AIFunction)tool;

        var args = FlowsArgsCustom(true, null);

        await func.InvokeAsync(args, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest.EnableVision);
    }

    [Fact]
    public async Task ExecuteRpaTool_PassesScreenshotPath()
    {
        RpaFlowRequest? capturedRequest = null;
        var mockService = new Mock<IRpaService>();
        mockService.Setup(x => x.ExecuteFlowAsync(It.IsAny<RpaFlowRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RpaFlowRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new RpaFlowResult { Success = true, CompletedSteps = 0 });

        var tool = RpaTools.CreateRpaTool(mockService.Object);
        var func = (AIFunction)tool;

        var args = FlowsArgsCustom(false, "/debug/screenshots");

        await func.InvokeAsync(args, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("/debug/screenshots", capturedRequest.ScreenshotPath);
    }

    [Fact]
    public async Task ExecuteRpaTool_ResponseMessage_OnSuccess_ContainsStepCount()
    {
        var mockService = new Mock<IRpaService>();
        mockService.Setup(x => x.ExecuteFlowAsync(It.IsAny<RpaFlowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RpaFlowResult { Success = true, CompletedSteps = 3, Error = null });

        var tool = RpaTools.CreateRpaTool(mockService.Object);
        var func = (AIFunction)tool;

        var args = FlowsArgs(
            new RpaWaitAction { Type = RpaActionType.Wait, DurationMs = 100 }
        );

        var result = await func.InvokeAsync(args, CancellationToken.None);

        var resultText = result?.ToString() ?? string.Empty;
        var doc = JsonDocument.Parse(resultText);
        var message = doc.RootElement.GetProperty("message").GetString();
        Assert.Contains("3", message);
        Assert.Contains("successfully", message);
    }

    [Fact]
    public async Task ExecuteRpaTool_ResponseMessage_OnFailure_ContainsErrorInfo()
    {
        var mockService = new Mock<IRpaService>();
        mockService.Setup(x => x.ExecuteFlowAsync(It.IsAny<RpaFlowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RpaFlowResult { Success = false, CompletedSteps = 2, Error = "Click failed: element not visible" });

        var tool = RpaTools.CreateRpaTool(mockService.Object);
        var func = (AIFunction)tool;

        var args = FlowsArgs(
            new RpaClickAction { Type = RpaActionType.Click, X = 100, Y = 200 }
        );

        var result = await func.InvokeAsync(args, CancellationToken.None);

        var resultText = result?.ToString() ?? string.Empty;
        var doc = JsonDocument.Parse(resultText);
        var message = doc.RootElement.GetProperty("message").GetString();
        Assert.Contains("Click failed", message);
    }
}
