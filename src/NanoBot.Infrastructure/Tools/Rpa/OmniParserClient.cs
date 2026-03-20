using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Tools.Rpa;

namespace NanoBot.Infrastructure.Tools.Rpa;

/// <summary>
/// OmniParser 客户端接口
/// </summary>
public interface IOmniParserClient : IAsyncDisposable
{
    /// <summary>
    /// 检测服务是否可用
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// 解析屏幕截图
    /// </summary>
    Task<OmniParserResult> ParseAsync(byte[] screenshot, CancellationToken ct = default);

    /// <summary>
    /// 启动服务（如果需要）
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// 停止服务
    /// </summary>
    Task StopAsync();
}

/// <summary>
/// OmniParser 客户端实现
/// </summary>
public class OmniParserClient : IOmniParserClient
{
    private readonly HttpClient _httpClient;
    private readonly int _port;
    private readonly ILogger<OmniParserClient>? _logger;
    private bool _disposed;

    public OmniParserClient(string host, int port, ILogger<OmniParserClient>? logger = null)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{host}:{port}"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _port = port;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "OmniParser health check failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<OmniParserResult> ParseAsync(byte[] screenshot, CancellationToken ct = default)
    {
        var base64 = Convert.ToBase64String(screenshot);

        var requestBody = new OmniParserParseRequest { Image = base64 };
        var response = await _httpClient.PostAsJsonAsync("/parse", requestBody, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OmniParserParseResponse>(ct);

        if (result == null)
        {
            throw new InvalidOperationException("OmniParser returned null result");
        }

        _logger?.LogDebug("OmniParser parsed {Count} elements", result.ParsedContent?.Count ?? 0);

        return new OmniParserResult
        {
            AnnotatedImage = result.AnnotatedImage,
            ParsedContent = result.ParsedContent?.Select(e => new OmniParserElement
            {
                Bbox = e.Bbox ?? Array.Empty<int>(),
                Label = e.Label ?? string.Empty,
                Type = e.Type ?? "unknown",
                Text = e.Text,
                Confidence = e.Confidence
            }).ToList() ?? []
        };
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _httpClient.Dispose();
        _disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// OmniParser 解析请求
/// </summary>
internal class OmniParserParseRequest
{
    [JsonPropertyName("image")]
    public string Image { get; init; } = string.Empty;
}

/// <summary>
/// OmniParser 解析响应
/// </summary>
internal class OmniParserParseResponse
{
    [JsonPropertyName("annotated_image")]
    public string? AnnotatedImage { get; init; }

    [JsonPropertyName("parsed_content")]
    public List<OmniParserParseElement>? ParsedContent { get; init; }
}

/// <summary>
/// OmniParser 解析元素
/// </summary>
internal class OmniParserParseElement
{
    [JsonPropertyName("bbox")]
    public int[]? Bbox { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}
