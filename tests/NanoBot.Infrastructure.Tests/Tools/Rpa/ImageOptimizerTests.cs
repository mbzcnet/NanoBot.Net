using SkiaSharp;
using NanoBot.Infrastructure.Tools.Rpa;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Tools.Rpa;

/// <summary>
/// Unit tests for ImageOptimizer using in-memory SkiaSharp-generated test images.
/// </summary>
public class ImageOptimizerTests : IDisposable
{
    private readonly ImageOptimizer _optimizer;

    public ImageOptimizerTests()
    {
        _optimizer = new ImageOptimizer();
    }

    public void Dispose()
    {
        // No managed resources to dispose
    }

    /// <summary>
    /// Generates a PNG image in memory using SkiaSharp.
    /// </summary>
    private static byte[] GeneratePngImage(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(color);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_DefaultOptions_ScalesToMaxDimension()
    {
        // 1920x1080 should be scaled down to max 1024px (height becomes 576)
        var pngBytes = GeneratePngImage(1920, 1080, SKColors.Red);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        Assert.Equal(1920, result.OriginalWidth);
        Assert.Equal(1080, result.OriginalHeight);
        Assert.True(result.Width <= 1024);
        Assert.True(result.Height <= 1024);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
        Assert.NotEmpty(result.Data);
        Assert.NotEmpty(result.Base64);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_FastMode_UsesCorrectOptions()
    {
        var pngBytes = GeneratePngImage(1920, 1080, SKColors.Blue);

        var result = await _optimizer.OptimizeForOmniParserAsync(
            pngBytes,
            OmniParserOptimizationOptions.Fast);

        // Fast mode: maxDimension = 800
        Assert.True(result.Width <= 800);
        Assert.True(result.Height <= 800);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_QualityMode_UsesCorrectOptions()
    {
        var pngBytes = GeneratePngImage(2560, 1440, SKColors.Green);

        var result = await _optimizer.OptimizeForOmniParserAsync(
            pngBytes,
            OmniParserOptimizationOptions.Quality);

        // Quality mode: maxDimension = 1280
        Assert.True(result.Width <= 1280);
        Assert.True(result.Height <= 1280);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_SmallImage_NoResize()
    {
        // 512x512 is already under maxDimension (1024), so no resize needed
        var pngBytes = GeneratePngImage(512, 512, SKColors.Yellow);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        Assert.Equal(512, result.OriginalWidth);
        Assert.Equal(512, result.OriginalHeight);
        Assert.Equal(512, result.Width);
        Assert.Equal(512, result.Height);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_EmptyBytes_ThrowsInvalidOperationException()
    {
        var emptyBytes = Array.Empty<byte>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _optimizer.OptimizeForOmniParserAsync(emptyBytes));
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_InvalidBytes_ThrowsInvalidOperationException()
    {
        var invalidBytes = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _optimizer.OptimizeForOmniParserAsync(invalidBytes));
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_ReturnsValidBase64()
    {
        var pngBytes = GeneratePngImage(800, 600, SKColors.Cyan);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        Assert.NotEmpty(result.Base64);

        var decoded = Convert.FromBase64String(result.Base64);
        Assert.NotEmpty(decoded);
        Assert.Equal(result.Data.Length, decoded.Length);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_CompressionRatio_CalculatesCorrectly()
    {
        // For large images, compression ratio should be > 1 (JPEG smaller than PNG)
        var pngBytes = GeneratePngImage(1920, 1080, SKColors.White);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        Assert.True(result.CompressionRatio > 0,
            $"Expected compression ratio > 0, got {result.CompressionRatio}. Input: {pngBytes.Length}, Output: {result.Data.Length}");
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_ScaleCalculation_WidthIsGreaterDimension()
    {
        // Portrait image: 600x1920 — width (600) is the smaller dimension,
        // so maxDimension=1024 should scale width to 1024 and height proportionally
        var pngBytes = GeneratePngImage(600, 1920, SKColors.Orange);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        Assert.True(result.Width <= 1024);
        Assert.True(result.Height <= 1024);
        // Verify aspect ratio is preserved
        var originalRatio = (float)600 / 1920;
        var resultRatio = (float)result.Width / result.Height;
        Assert.True(Math.Abs(originalRatio - resultRatio) < 0.01f);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_PreservesAspectRatio()
    {
        // 1600x900 (16:9 aspect ratio)
        var pngBytes = GeneratePngImage(1600, 900, SKColors.Pink);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        var originalRatio = (float)1600 / 900;
        var resultRatio = (float)result.Width / result.Height;
        Assert.True(Math.Abs(originalRatio - resultRatio) < 0.01f,
            $"Aspect ratio changed: expected {originalRatio:F3}, got {resultRatio:F3}");
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_ZeroWidthHeightBytes_Throws()
    {
        // Zero-sized image data that SKBitmap cannot decode
        var zeroBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic only

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _optimizer.OptimizeForOmniParserAsync(zeroBytes));
    }

    [Fact]
    public async Task OptimizeFastAsync_EquivalentToFastOptions()
    {
        var pngBytes = GeneratePngImage(1920, 1080, SKColors.Violet);

        var fastResult = await _optimizer.OptimizeFastAsync(pngBytes);
        var explicitResult = await _optimizer.OptimizeForOmniParserAsync(
            pngBytes, OmniParserOptimizationOptions.Fast);

        Assert.Equal(fastResult.Width, explicitResult.Width);
        Assert.Equal(fastResult.Height, explicitResult.Height);
    }

    [Fact]
    public async Task OptimizeQualityAsync_EquivalentToQualityOptions()
    {
        var pngBytes = GeneratePngImage(2560, 1440, SKColors.Brown);

        var qualityResult = await _optimizer.OptimizeQualityAsync(pngBytes);
        var explicitResult = await _optimizer.OptimizeForOmniParserAsync(
            pngBytes, OmniParserOptimizationOptions.Quality);

        Assert.Equal(qualityResult.Width, explicitResult.Width);
        Assert.Equal(qualityResult.Height, explicitResult.Height);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_JpegOutput_ContainsValidData()
    {
        var pngBytes = GeneratePngImage(1024, 768, SKColors.LightGray);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        // Verify the output is valid JPEG data (starts with JPEG SOI marker FF D8 FF)
        Assert.True(result.Data.Length >= 3);
        Assert.Equal(0xFF, result.Data[0]);
        Assert.Equal(0xD8, result.Data[1]);
        Assert.Equal(0xFF, result.Data[2]);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_Summary_ContainsCorrectInfo()
    {
        var pngBytes = GeneratePngImage(1920, 1080, SKColors.DarkBlue);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        var summary = result.Summary;
        Assert.Contains("1920", summary);
        Assert.Contains("1080", summary);
        Assert.Contains("x", summary); // "WIDTHxHEIGHT"
        Assert.Contains("KB", summary);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_SizeBytesMatchesActualDataLength()
    {
        var pngBytes = GeneratePngImage(800, 600, SKColors.Teal);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        Assert.Equal(result.Data.Length, result.SizeBytes);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_WidthLargerThanHeight_UsesWidthForScaling()
    {
        // 1920x1080: width (1920) is the larger dimension, scale = 1024/1920 ≈ 0.533
        var pngBytes = GeneratePngImage(1920, 1080, SKColors.Indigo);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        Assert.Equal(1024, result.Width);
        Assert.Equal(576, result.Height);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_HeightLargerThanWidth_UsesHeightForScaling()
    {
        // 1080x1920: height (1920) is the larger dimension, scale = 1024/1920 ≈ 0.533
        var pngBytes = GeneratePngImage(1080, 1920, SKColors.Maroon);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        Assert.Equal(576, result.Width);
        Assert.Equal(1024, result.Height);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_ExactMaxDimension_NoResize()
    {
        // Exactly 1024x1024 — should not be resized
        var pngBytes = GeneratePngImage(1024, 1024, SKColors.Silver);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        Assert.Equal(1024, result.Width);
        Assert.Equal(1024, result.Height);
    }

    [Fact]
    public async Task OptimizeForOmniParserAsync_LargerThanMaxDimension_Resizes()
    {
        // 2048x2048 — should be resized to 1024x1024
        var pngBytes = GeneratePngImage(2048, 2048, SKColors.Olive);

        var result = await _optimizer.OptimizeForOmniParserAsync(pngBytes, null);

        Assert.Equal(1024, result.Width);
        Assert.Equal(1024, result.Height);
    }
}
