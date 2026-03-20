using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace NanoBot.Infrastructure.Tools.Rpa;

/// <summary>
/// OmniParser 截图优化选项
/// </summary>
public class OmniParserOptimizationOptions
{
    /// <summary>
    /// 最大边长（像素），默认 1024
    /// </summary>
    public int MaxDimension { get; set; } = 1024;

    /// <summary>
    /// JPEG 质量（0-100），默认 70
    /// </summary>
    public int JpegQuality { get; set; } = 70;

    /// <summary>
    /// 移除元数据，默认 true
    /// </summary>
    public bool StripMetadata { get; set; } = true;

    /// <summary>
    /// 默认优化选项
    /// </summary>
    public static OmniParserOptimizationOptions Default => new()
    {
        MaxDimension = 1024,
        JpegQuality = 70,
        StripMetadata = true
    };

    /// <summary>
    /// 快速模式 - 最小化延迟
    /// </summary>
    public static OmniParserOptimizationOptions Fast => new()
    {
        MaxDimension = 800,
        JpegQuality = 60,
        StripMetadata = true
    };

    /// <summary>
    /// 质量模式 - 保留更多细节
    /// </summary>
    public static OmniParserOptimizationOptions Quality => new()
    {
        MaxDimension = 1280,
        JpegQuality = 85,
        StripMetadata = true
    };
}

/// <summary>
/// 优化后的图像
/// </summary>
public class OptimizedImage
{
    /// <summary>
    /// 图像数据
    /// </summary>
    public byte[] Data { get; init; } = [];

    /// <summary>
    /// 优化后的宽度
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// 优化后的高度
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// 原始宽度
    /// </summary>
    public int OriginalWidth { get; init; }

    /// <summary>
    /// 原始高度
    /// </summary>
    public int OriginalHeight { get; init; }

    /// <summary>
    /// 优化后的大小（字节）
    /// </summary>
    public int SizeBytes { get; init; }

    /// <summary>
    /// 压缩比
    /// </summary>
    public float CompressionRatio { get; init; }

    /// <summary>
    /// Base64 编码
    /// </summary>
    public string Base64 { get; init; } = string.Empty;

    /// <summary>
    /// 获取优化摘要
    /// </summary>
    public string Summary => $"Optimized: {OriginalWidth}x{OriginalHeight} -> {Width}x{Height}, " +
                             $"Size: {SizeBytes / 1024}KB (ratio: {CompressionRatio:F1}x)";
}

/// <summary>
/// 图像优化器
/// </summary>
public class ImageOptimizer
{
    private readonly ILogger<ImageOptimizer>? _logger;

    public ImageOptimizer(ILogger<ImageOptimizer>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 优化截图以减少 OmniParser 处理负担
    /// </summary>
    /// <param name="rawImage">原始 PNG 图像数据</param>
    /// <param name="options">优化选项，null 使用默认</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>优化后的图像</returns>
    public Task<OptimizedImage> OptimizeForOmniParserAsync(
        byte[] rawImage,
        OmniParserOptimizationOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= OmniParserOptimizationOptions.Default;

        return Task.Run(() =>
        {
            using var inputStream = new MemoryStream(rawImage);
            using var original = SKBitmap.Decode(inputStream);

            if (original == null)
            {
                throw new InvalidOperationException("Failed to decode image");
            }

            var originalWidth = original.Width;
            var originalHeight = original.Height;

            // 1. 缩放到最大尺寸
            var maxDimension = options.MaxDimension;
            var scale = Math.Min(1.0f,
                (float)maxDimension / Math.Max(originalWidth, originalHeight));

            int newWidth, newHeight;
            SKBitmap? resized;

            if (scale < 1.0f)
            {
                newWidth = (int)(originalWidth * scale);
                newHeight = (int)(originalHeight * scale);

                resized = original.Resize(
                    new SKImageInfo(newWidth, newHeight),
                    SKSamplingOptions.Default);
            }
            else
            {
                newWidth = originalWidth;
                newHeight = originalHeight;
                resized = original;
            }

            // 2. 转换为 JPEG
            using var outputStream = new MemoryStream();
            using var image = SKImage.FromBitmap(resized!);
            image.Encode(SKEncodedImageFormat.Jpeg, options.JpegQuality).SaveTo(outputStream);
            var optimizedBytes = outputStream.ToArray();

            if (resized != original)
            {
                resized?.Dispose();
            }

            var result = new OptimizedImage
            {
                Data = optimizedBytes,
                Width = newWidth,
                Height = newHeight,
                OriginalWidth = originalWidth,
                OriginalHeight = originalHeight,
                SizeBytes = optimizedBytes.Length,
                CompressionRatio = rawImage.Length > 0 ? (float)rawImage.Length / optimizedBytes.Length : 0,
                Base64 = Convert.ToBase64String(optimizedBytes)
            };

            _logger?.LogDebug("Image optimized: {Summary}", result.Summary);

            return result;
        }, ct);
    }

    /// <summary>
    /// 快速优化（使用快速模式）
    /// </summary>
    public Task<OptimizedImage> OptimizeFastAsync(byte[] rawImage, CancellationToken ct = default) =>
        OptimizeForOmniParserAsync(rawImage, OmniParserOptimizationOptions.Fast, ct);

    /// <summary>
    /// 质量优先优化
    /// </summary>
    public Task<OptimizedImage> OptimizeQualityAsync(byte[] rawImage, CancellationToken ct = default) =>
        OptimizeForOmniParserAsync(rawImage, OmniParserOptimizationOptions.Quality, ct);
}
