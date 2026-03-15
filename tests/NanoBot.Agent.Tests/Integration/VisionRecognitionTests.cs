using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Providers;
using Xunit;
using Xunit.Abstractions;

namespace NanoBot.Agent.Tests.Integration;

/// <summary>
/// Vision recognition tests using benchmark images from src/benchmark
/// Tests image description and OCR capabilities using real LLM models with vision support
/// </summary>
public class VisionRecognitionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _benchmarkPath;

    public VisionRecognitionTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        _benchmarkPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
            "src", "benchmark");
        _benchmarkPath = Path.GetFullPath(_benchmarkPath);

        _output.WriteLine($"Benchmark path: {_benchmarkPath}");
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
    }

    private IChatClient CreateVisionChatClient(string provider = "openai", string model = "gpt-4o-mini")
    {
        var apiKey = Environment.GetEnvironmentVariable($"{provider.ToUpperInvariant()}_API_KEY");
        var apiBase = Environment.GetEnvironmentVariable($"{provider.ToUpperInvariant()}_API_BASE");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _output.WriteLine($"Warning: {provider.ToUpperInvariant()}_API_KEY not set. Skipping test.");
            throw new SkipTestException($"API key for {provider} not configured");
        }

        var logger = _loggerFactory.CreateLogger<ChatClientFactory>();
        var factory = new ChatClientFactory(logger);

        return factory.CreateChatClient(provider, model, apiKey, apiBase);
    }

    private string GetImagePath(string fileName)
    {
        var path = Path.Combine(_benchmarkPath, fileName);
        _output.WriteLine($"Looking for image at: {path}");
        
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Test image not found: {path}");
        }
        
        return path;
    }

    private async Task<string> SendImageToModelAsync(IChatClient chatClient, string imagePath, string prompt)
    {
        var imageBytes = await File.ReadAllBytesAsync(imagePath);
        var mediaType = GetImageMediaType(imagePath);

        var contents = new List<AIContent>
        {
            new DataContent(imageBytes, mediaType),
            new TextContent(prompt)
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are an AI assistant with vision capabilities. Please describe images accurately and truthfully."),
            new(ChatRole.User, contents)
        };

        _output.WriteLine($"Sending image {Path.GetFileName(imagePath)} to model with prompt: {prompt}");

        var response = await chatClient.GetResponseAsync(messages);

        var responseText = response.Text ?? "";
        _output.WriteLine($"Model response: {responseText}");

        return responseText;
    }

    private string GetImageMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "image/png"
        };
    }

    [Fact(Skip = "Requires real API key with vision support - enable for integration testing")]
    public async Task Vision_ShouldDescribeSimpleShapesAndColors()
    {
        // Arrange
        using var chatClient = CreateVisionChatClient("openai", "gpt-4o-mini");
        var imagePath = GetImagePath("vision_things_v1.jpg");
        
        // Expected content from vision_things_v1.txt
        var expectedItems = new[] { "circle", "square", "cube", "mug", "phone", "apple", "blue", "red", "green" };

        // Act
        var response = await SendImageToModelAsync(
            chatClient,
            imagePath,
            "Describe what you see in this image in detail. List all objects, shapes, and colors you can identify.");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        var responseLower = response.ToLowerInvariant();
        var matchedCount = expectedItems.Count(expected => responseLower.Contains(expected.ToLowerInvariant()));
        
        _output.WriteLine($"Matched {matchedCount}/{expectedItems.Length} expected items");
        
        // Require at least 50% of expected items to be mentioned
        Assert.True(matchedCount >= expectedItems.Length / 2,
            $"Expected at least {expectedItems.Length / 2} items to be mentioned, but only found {matchedCount}");
    }

    [Fact(Skip = "Requires real API key with vision support - enable for integration testing")]
    public async Task Vision_ShouldExtractTextFromImage()
    {
        // Arrange
        using var chatClient = CreateVisionChatClient("openai", "gpt-4o-mini");
        var imagePath = GetImagePath("vison_ocr_v1.jpg");
        
        // Expected text content from vison_ocr_v1.txt
        var expectedTexts = new[] { "AI Image Recognition Test", "bar chart", "line graph" };

        // Act
        var response = await SendImageToModelAsync(
            chatClient,
            imagePath,
            "Extract all text from this image. Describe any charts or graphs you see.");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        var responseLower = response.ToLowerInvariant();
        var matchedCount = expectedTexts.Count(expected => responseLower.Contains(expected.ToLowerInvariant()));
        
        _output.WriteLine($"Matched {matchedCount}/{expectedTexts.Length} expected text elements");
        
        // Require at least 2 out of 3 expected texts to be found
        Assert.True(matchedCount >= 2,
            $"Expected at least 2 text elements to be extracted, but only found {matchedCount}");
    }

    [Fact(Skip = "Requires real API key with vision support - enable for integration testing")]
    public async Task Vision_ShouldCountObjectsInImage()
    {
        // Arrange
        using var chatClient = CreateVisionChatClient("openai", "gpt-4o-mini");
        var imagePath = GetImagePath("vision_things_v1.jpg");

        // Act
        var response = await SendImageToModelAsync(
            chatClient,
            imagePath,
            "How many distinct objects can you see in this image? List them all and provide a count.");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        // Check if response contains a number or count
        var containsNumber = System.Text.RegularExpressions.Regex.IsMatch(response, @"\d+");
        Assert.True(containsNumber, "Response should contain a number or count of objects");
        
        _output.WriteLine($"Object counting response: {response}");
    }

    [Fact(Skip = "Requires real API key with vision support - enable for integration testing")]
    public async Task Vision_ShouldIdentifySpatialRelationships()
    {
        // Arrange
        using var chatClient = CreateVisionChatClient("openai", "gpt-4o-mini");
        var imagePath = GetImagePath("vision_things_v1.jpg");

        // Act
        var response = await SendImageToModelAsync(
            chatClient,
            imagePath,
            "Describe the spatial arrangement of objects in this image. What is at the top, middle, and bottom?");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        var responseLower = response.ToLowerInvariant();
        var hasSpatialTerms = new[] { "top", "middle", "bottom", "above", "below", "left", "right" }
            .Any(term => responseLower.Contains(term));
        
        Assert.True(hasSpatialTerms, "Response should contain spatial relationship terms");
        _output.WriteLine($"Spatial description: {response}");
    }

    [Fact(Skip = "Requires real API key with vision support - enable for integration testing")]
    public async Task Vision_ShouldRecognizeChartTypes()
    {
        // Arrange
        using var chatClient = CreateVisionChatClient("openai", "gpt-4o-mini");
        var imagePath = GetImagePath("vison_ocr_v1.jpg");

        // Act
        var response = await SendImageToModelAsync(
            chatClient,
            imagePath,
            "What types of charts or graphs do you see in this image? Describe their purpose.");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        var responseLower = response.ToLowerInvariant();
        var hasChartTerms = new[] { "bar", "chart", "graph", "line", "data", "visualization" }
            .Any(term => responseLower.Contains(term));
        
        Assert.True(hasChartTerms, "Response should identify chart/graph types");
        _output.WriteLine($"Chart recognition response: {response}");
    }

    [Theory(Skip = "Requires real API key with vision support - enable for integration testing")]
    [InlineData("openai", "gpt-4o-mini")]
    [InlineData("anthropic", "claude-3-haiku-20240307")]
    public async Task Vision_DifferentModels_ShouldDescribeImage(string provider, string model)
    {
        // Arrange
        using var chatClient = CreateVisionChatClient(provider, model);
        var imagePath = GetImagePath("vision_things_v1.jpg");

        // Act
        var response = await SendImageToModelAsync(
            chatClient,
            imagePath,
            "Briefly describe this image in 2-3 sentences.");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        
        var wordCount = response.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        _output.WriteLine($"{provider}/{model} response ({wordCount} words): {response}");
        
        // Response should be reasonable length (not too short, not too long)
        Assert.True(wordCount >= 10 && wordCount <= 100,
            $"Response should be 10-100 words, got {wordCount}");
    }

    [Fact(Skip = "Requires real API key with vision support - enable for integration testing")]
    public async Task Vision_ShouldHandleMultipleImagesSequentially()
    {
        // Arrange
        using var chatClient = CreateVisionChatClient("openai", "gpt-4o-mini");
        var imagePaths = new[]
        {
            GetImagePath("vision_things_v1.jpg"),
            GetImagePath("vison_ocr_v1.jpg")
        };

        // Act & Assert
        foreach (var imagePath in imagePaths)
        {
            var response = await SendImageToModelAsync(
                chatClient,
                imagePath,
                "What do you see in this image?");

            Assert.NotNull(response);
            Assert.NotEmpty(response);
            _output.WriteLine($"Image {Path.GetFileName(imagePath)}: {response[..Math.Min(200, response.Length)]}...");
        }
    }

    [Fact(Skip = "Requires real API key with vision support - enable for integration testing")]
    public async Task Vision_ShouldAnswerQuestionsAboutImage()
    {
        // Arrange
        using var chatClient = CreateVisionChatClient("openai", "gpt-4o-mini");
        var imagePath = GetImagePath("vision_things_v1.jpg");

        // First, get general description
        var descriptionResponse = await SendImageToModelAsync(
            chatClient,
            imagePath,
            "Describe this image.");

        Assert.NotNull(descriptionResponse);
        _output.WriteLine($"Initial description: {descriptionResponse}");

        // Act - Ask specific follow-up question
        var questionResponse = await SendImageToModelAsync(
            chatClient,
            imagePath,
            "What color is the circle in this image?");

        // Assert
        Assert.NotNull(questionResponse);
        Assert.NotEmpty(questionResponse);
        Assert.Contains("blue", questionResponse.ToLowerInvariant());
        
        _output.WriteLine($"Question answer: {questionResponse}");
    }
}
