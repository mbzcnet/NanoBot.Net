using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Agent;
using NanoBot.Core.Sessions;
using NanoBot.WebUI.Services;
using Xunit;

namespace NanoBot.WebUI.Tests;

public class AgentServiceStreamingTests
{
    [Fact]
    public async Task SendMessageStreamingAsync_ShouldExposeToolCallAndToolResultChunks()
    {
        var runtime = new Mock<IAgentRuntime>();
        var sessionManager = new Mock<ISessionManager>();
        var logger = new Mock<ILogger<AgentService>>();

        runtime.Setup(r => r.ProcessDirectStreamingAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateUpdates());

        var service = new AgentService(runtime.Object, sessionManager.Object, null, logger.Object);

        var chunks = new List<AgentResponseChunk>();
        await foreach (var chunk in service.SendMessageStreamingAsync("s1", "open bing"))
        {
            chunks.Add(chunk);
        }

        chunks.Any(c => c.ToolCallDetails != null && c.ToolCallDetails.Name == "browser").Should().BeTrue();

        var toolCallChunk = chunks.First(c => c.ToolCallDetails is not null);
        toolCallChunk.ToolCallDetails!.CallId.Should().Be("call_1");
        toolCallChunk.ToolCallDetails.Arguments.Should().Contain("open");

        chunks.Should().Contain(c => c.IsToolResult && c.ToolResultCallId == "call_1");
        chunks.Should().Contain(c => c.Content.Contains("done", StringComparison.OrdinalIgnoreCase));
    }

    private static async IAsyncEnumerable<AgentResponseUpdate> CreateUpdates()
    {
        var callUpdate = new AgentResponseUpdate
        {
            Role = ChatRole.Assistant
        };
        callUpdate.Contents.Add(new FunctionCallContent(
            "call_1",
            "browser",
            new Dictionary<string, object?>
            {
                ["action"] = "open",
                ["targetUrl"] = "https://www.bing.com"
            }));
        yield return callUpdate;

        var resultUpdate = new AgentResponseUpdate
        {
            Role = ChatRole.Tool,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["_tool_result"] = true,
                ["tool_call_id"] = "call_1"
            }
        };
        resultUpdate.Contents.Add(new TextContent("{\"ok\":true}"));
        yield return resultUpdate;

        var finalUpdate = new AgentResponseUpdate
        {
            Role = ChatRole.Assistant
        };
        finalUpdate.Contents.Add(new TextContent("done"));
        yield return finalUpdate;

        await Task.CompletedTask;
    }
}
