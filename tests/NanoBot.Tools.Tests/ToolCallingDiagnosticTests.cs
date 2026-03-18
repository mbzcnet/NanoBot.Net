// Copyright (c) NanoBot. All rights reserved.

using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Providers;
using Xunit;
using Xunit.Abstractions;

namespace NanoBot.Tools.Tests;

/// <summary>
/// Diagnostic tests for verifying tool calling functionality.
/// References Microsoft.Agents.AI samples/01-get-started/02_add_tools/Program.cs
/// </summary>
public class ToolCallingDiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _ollamaApiBase = "http://172.16.3.220:11435/v1";
    private readonly string _ollamaModel = "qwen3.5";
    private readonly IChatClient _chatClient;

    public ToolCallingDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        var logger = _loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(logger);
        _chatClient = factory.CreateChatClient("openai", _ollamaModel, "ollama", _ollamaApiBase);

        _output.WriteLine($"ChatClient type: {_chatClient.GetType().FullName}");
        _output.WriteLine($"Has FunctionInvokingChatClient: {_chatClient.GetService<FunctionInvokingChatClient>() != null}");
    }

    public void Dispose()
    {
        _chatClient?.Dispose();
        _loggerFactory?.Dispose();
    }

    private static bool EnsureEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("NANOBOT_OLLAMA_INTEGRATION"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    [Description("Get the weather for a given location.")]
    static string GetWeather([Description("The location to get the weather for.")] string location)
        => $"The weather in {location} is cloudy with a high of 15°C.";

    [Description("List files in a directory.")]
    static string ListFiles([Description("The directory path.")] string path)
        => $"Files in {path}: file1.txt, file2.log, subdir/";

    [Fact]
    public async Task Test1_DirectChatClientCall_ShouldCallTool()
    {
        if (!EnsureEnabled()) return;

        _output.WriteLine("=== Test 1: Direct IChatClient call ===");

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(GetWeather, new AIFunctionFactoryOptions
            {
                Name = "get_weather",
                Description = "Get the weather for a given location."
            })
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant with access to tools."),
            new(ChatRole.User, "What's the weather in Beijing?")
        };

        var options = new ChatOptions { Tools = tools };

        _output.WriteLine($"Sending request with {tools.Count} tools...");

        var response = await _chatClient.GetResponseAsync(messages, options);

        var responseText = response.Text ?? "";
        var hasToolCall = response.Messages?.Any(m => m.Contents.Any(c => c is FunctionCallContent)) == true;

        _output.WriteLine($"Response: {Truncate(responseText, 200)}");
        _output.WriteLine($"Has tool call: {hasToolCall}");

        if (hasToolCall)
        {
            foreach (var msg in response.Messages ?? [])
            {
                var calls = msg.Contents.OfType<FunctionCallContent>();
                foreach (var call in calls)
                {
                    _output.WriteLine($"  Tool: {call.Name}, Args: {call.Arguments?.Count ?? 0}");
                }
            }
        }

        Assert.True(hasToolCall, "Tool should be called for weather query");
    }

    [Fact]
    public async Task Test2_AsAIAgent_ShouldCallTool()
    {
        if (!EnsureEnabled()) return;

        _output.WriteLine("=== Test 2: ChatClientAgent with AsAIAgent ===");

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(GetWeather, new AIFunctionFactoryOptions
            {
                Name = "get_weather",
                Description = "Get the weather for a given location."
            })
        };

        var agent = _chatClient.AsAIAgent(
            instructions: "You are a helpful assistant with access to tools. Use tools when they can help answer the user's question.",
            name: "DiagnosticAgent",
            tools: tools,
            loggerFactory: _loggerFactory);

        _output.WriteLine($"Agent created: {agent.Name}");
        _output.WriteLine($"Agent Instructions length: {agent.Instructions?.Length ?? 0}");

        var response = await agent.RunAsync("What's the weather in Shanghai?");

        var responseText = response.Text ?? "";
        var hasToolCall = response.Messages.Any(m => m.Contents.Any(c => c is FunctionCallContent));

        _output.WriteLine($"Response: {Truncate(responseText, 200)}");
        _output.WriteLine($"Has tool call: {hasToolCall}");

        if (hasToolCall)
        {
            foreach (var msg in response.Messages)
            {
                var calls = msg.Contents.OfType<FunctionCallContent>();
                foreach (var call in calls)
                {
                    _output.WriteLine($"  Tool: {call.Name}, Args: {call.Arguments?.Count ?? 0}");
                }
            }
        }

        Assert.True(hasToolCall, "Tool should be called for weather query");
    }

    [Fact]
    public async Task Test3_ChatClientAgentConstructor_ShouldCallTool()
    {
        if (!EnsureEnabled()) return;

        _output.WriteLine("=== Test 3: ChatClientAgent with constructor ===");

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(ListFiles, new AIFunctionFactoryOptions
            {
                Name = "list_files",
                Description = "List files in a directory."
            })
        };

        var agent = new ChatClientAgent(_chatClient, new ChatClientAgentOptions
        {
            Name = "DiagnosticAgent2",
            Description = "Test agent",
            ChatOptions = new ChatOptions
            {
                Instructions = "You are a helpful assistant with access to tools. Use tools when they can help answer the user's question.",
                Tools = tools
            }
        }, _loggerFactory);

        _output.WriteLine($"Agent created: {agent.Name}");
        _output.WriteLine($"Agent Instructions length: {agent.Instructions?.Length ?? 0}");

        var response = await agent.RunAsync("List files in /tmp directory");

        var responseText = response.Text ?? "";
        var hasToolCall = response.Messages.Any(m => m.Contents.Any(c => c is FunctionCallContent));

        _output.WriteLine($"Response: {Truncate(responseText, 200)}");
        _output.WriteLine($"Has tool call: {hasToolCall}");

        if (hasToolCall)
        {
            foreach (var msg in response.Messages)
            {
                var calls = msg.Contents.OfType<FunctionCallContent>();
                foreach (var call in calls)
                {
                    _output.WriteLine($"  Tool: {call.Name}, Args: {call.Arguments?.Count ?? 0}");
                }
            }
        }

        Assert.True(hasToolCall, "Tool should be called for file listing query");
    }

    [Fact]
    public async Task Test4_ChatClientAgentWithRunOptions_ShouldCallTool()
    {
        if (!EnsureEnabled()) return;

        _output.WriteLine("=== Test 4: ChatClientAgent with RunOptions.Tools ===");

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(GetWeather, new AIFunctionFactoryOptions
            {
                Name = "get_weather",
                Description = "Get the weather for a given location."
            })
        };

        var agent = new ChatClientAgent(_chatClient, "You are a helpful assistant.", loggerFactory: _loggerFactory);

        _output.WriteLine($"Agent created (no tools in constructor)");

        var runOptions = new ChatClientAgentRunOptions
        {
            ChatOptions = new ChatOptions
            {
                Tools = tools
            }
        };

        var response = await agent.RunAsync("What's the weather in Tokyo?", options: runOptions);

        var responseText = response.Text ?? "";
        var hasToolCall = response.Messages.Any(m => m.Contents.Any(c => c is FunctionCallContent));

        _output.WriteLine($"Response: {Truncate(responseText, 200)}");
        _output.WriteLine($"Has tool call: {hasToolCall}");

        if (hasToolCall)
        {
            foreach (var msg in response.Messages)
            {
                var calls = msg.Contents.OfType<FunctionCallContent>();
                foreach (var call in calls)
                {
                    _output.WriteLine($"  Tool: {call.Name}, Args: {call.Arguments?.Count ?? 0}");
                }
            }
        }

        Assert.True(hasToolCall, "Tool should be called for weather query");
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text ?? "";
        return text[..maxLength] + "...";
    }
}