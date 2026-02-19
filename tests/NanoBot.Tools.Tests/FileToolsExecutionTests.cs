using NanoBot.Tools.BuiltIn;
using Xunit;
using System.Reflection;

namespace NanoBot.Tools.Tests;

public class FileToolsExecutionTests : IDisposable
{
    private readonly string _testDirectory;

    public FileToolsExecutionTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"filetools_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, recursive: true); } catch { }
        }
    }

    private static async Task<string> CallReadFileAsync(string path, int? startLine = null, int? endLine = null, string? allowedDir = null)
    {
        var method = typeof(FileTools).GetMethod("ReadFileAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<string>)method.Invoke(null, new object[] { path, startLine, endLine, allowedDir, CancellationToken.None })!;
    }

    private static async Task<string> CallWriteFileAsync(string path, string content, string? allowedDir = null)
    {
        var method = typeof(FileTools).GetMethod("WriteFileAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<string>)method.Invoke(null, new object[] { path, content, allowedDir, CancellationToken.None })!;
    }

    private static async Task<string> CallEditFileAsync(string path, string oldStr, string newStr, string? allowedDir = null)
    {
        var method = typeof(FileTools).GetMethod("EditFileAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<string>)method.Invoke(null, new object[] { path, oldStr, newStr, allowedDir, CancellationToken.None })!;
    }

    private static string CallListDir(string path, bool recursive = false, string? allowedDir = null)
    {
        var method = typeof(FileTools).GetMethod("ListDir", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { path, recursive, allowedDir })!;
    }

    #region ReadFile Tests

    [Fact]
    public async Task ReadFile_WithValidPath_ReturnsContent()
    {
        var testFile = Path.Combine(_testDirectory, "test.txt");
        var content = "Hello, World!";
        await File.WriteAllTextAsync(testFile, content);

        var result = await CallReadFileAsync(testFile, allowedDir: _testDirectory);

        Assert.Equal(content, result);
    }

    [Fact]
    public async Task ReadFile_WithLineRange_ReturnsPartialContent()
    {
        var testFile = Path.Combine(_testDirectory, "lines.txt");
        var lines = new[] { "Line 1", "Line 2", "Line 3", "Line 4", "Line 5" };
        await File.WriteAllLinesAsync(testFile, lines);

        var result = await CallReadFileAsync(testFile, startLine: 2, endLine: 4, allowedDir: _testDirectory);

        Assert.Contains("Line 2", result);
        Assert.Contains("Line 3", result);
        Assert.DoesNotContain("Line 1", result);
        Assert.DoesNotContain("Line 5", result);
    }

    [Fact]
    public async Task ReadFile_WithInvalidPath_ReturnsError()
    {
        var result = await CallReadFileAsync(
            Path.Combine(_testDirectory, "nonexistent.txt"), 
            allowedDir: _testDirectory);

        Assert.Contains("Error", result);
        Assert.Contains("not found", result);
    }

    [Fact]
    public async Task ReadFile_RespectsAllowedDirectories()
    {
        var result = await CallReadFileAsync(
            Path.Combine(_testDirectory, "..", "outside.txt"),
            allowedDir: _testDirectory);

        Assert.Contains("Error", result);
        Assert.Contains("outside allowed directory", result);
    }

    [Fact]
    public async Task ReadFile_WithEmptyFile_ReturnsEmptyString()
    {
        var testFile = Path.Combine(_testDirectory, "empty.txt");
        await File.WriteAllTextAsync(testFile, "");

        var result = await CallReadFileAsync(testFile, allowedDir: _testDirectory);

        Assert.Equal("", result);
    }

    #endregion

    #region WriteFile Tests

    [Fact]
    public async Task WriteFile_CreatesNewFile()
    {
        var filePath = Path.Combine(_testDirectory, "newfile.txt");
        var content = "New content";

        var result = await CallWriteFileAsync(filePath, content, _testDirectory);

        Assert.True(File.Exists(filePath));
        Assert.Equal(content, await File.ReadAllTextAsync(filePath));
        Assert.Contains("Successfully wrote", result);
    }

    [Fact]
    public async Task WriteFile_OverwritesExistingFile()
    {
        var testFile = Path.Combine(_testDirectory, "existing.txt");
        await File.WriteAllTextAsync(testFile, "Original content");

        var result = await CallWriteFileAsync(testFile, "New content", _testDirectory);

        Assert.Equal("New content", await File.ReadAllTextAsync(testFile));
    }

    [Fact]
    public async Task WriteFile_CreatesParentDirectories()
    {
        var filePath = Path.Combine(_testDirectory, "subdir1", "subdir2", "nested.txt");
        var content = "Nested content";

        var result = await CallWriteFileAsync(filePath, content, _testDirectory);

        Assert.True(File.Exists(filePath));
        Assert.Equal(content, await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task WriteFile_RespectsAllowedDirectories()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "outside.txt");

        var result = await CallWriteFileAsync(filePath, "Should fail", _testDirectory);

        Assert.Contains("Error", result);
        Assert.Contains("outside allowed directory", result);
    }

    #endregion

    #region EditFile Tests

    [Fact]
    public async Task EditFile_ReplacesTextCorrectly()
    {
        var testFile = Path.Combine(_testDirectory, "edit.txt");
        var originalContent = "Hello World, Hello Universe";
        await File.WriteAllTextAsync(testFile, originalContent);

        var result = await CallEditFileAsync(testFile, "World", "NanoBot", _testDirectory);

        var newContent = await File.ReadAllTextAsync(testFile);
        Assert.Equal("Hello NanoBot, Hello Universe", newContent);
        Assert.Contains("Successfully edited", result);
    }

    [Fact]
    public async Task EditFile_WithNoMatch_ReturnsError()
    {
        var testFile = Path.Combine(_testDirectory, "noedit.txt");
        await File.WriteAllTextAsync(testFile, "Hello World");

        var result = await CallEditFileAsync(testFile, "Nonexistent", "Something", _testDirectory);

        Assert.Contains("Error", result);
        Assert.Contains("Could not find", result);
    }

    [Fact]
    public async Task EditFile_PreservesOtherContent()
    {
        var testFile = Path.Combine(_testDirectory, "multi.txt");
        await File.WriteAllTextAsync(testFile, "Line 1\nLine 2\nLine 3");

        await CallEditFileAsync(testFile, "Line 2", "Modified Line 2", _testDirectory);

        var content = await File.ReadAllTextAsync(testFile);
        Assert.Contains("Line 1", content);
        Assert.Contains("Modified Line 2", content);
        Assert.Contains("Line 3", content);
    }

    [Fact]
    public async Task EditFile_WithNonexistentFile_ReturnsError()
    {
        var result = await CallEditFileAsync(
            Path.Combine(_testDirectory, "notexists.txt"), 
            "old", "new", _testDirectory);

        Assert.Contains("Error", result);
        Assert.Contains("not found", result);
    }

    #endregion

    #region ListDir Tests

    [Fact]
    public async Task ListDir_ReturnsDirectoryContents()
    {
        Directory.CreateDirectory(Path.Combine(_testDirectory, "subdir"));
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "file1.txt"), "");
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "file2.txt"), "");

        var result = CallListDir(_testDirectory, recursive: false, allowedDir: _testDirectory);

        Assert.Contains("file1.txt", result);
        Assert.Contains("file2.txt", result);
        Assert.Contains("subdir", result);
    }

    [Fact]
    public async Task ListDir_WithRecursive_ReturnsAllEntries()
    {
        Directory.CreateDirectory(Path.Combine(_testDirectory, "subdir", "nested"));
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "root.txt"), "");
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "subdir", "nested", "deep.txt"), "");

        var result = CallListDir(_testDirectory, recursive: true, allowedDir: _testDirectory);

        Assert.Contains("root.txt", result);
        Assert.Contains("nested", result);
        Assert.Contains("deep.txt", result);
    }

    [Fact]
    public void ListDir_WithInvalidPath_ReturnsError()
    {
        var result = CallListDir(
            Path.Combine(_testDirectory, "nonexistent"), 
            recursive: false, 
            allowedDir: _testDirectory);

        Assert.Contains("Error", result);
        Assert.Contains("not found", result);
    }

    [Fact]
    public void ListDir_RespectsAllowedDirectories()
    {
        var result = CallListDir(
            Path.Combine(_testDirectory, "..", "outside"),
            recursive: false, 
            allowedDir: _testDirectory);

        Assert.Contains("Error", result);
        Assert.Contains("outside allowed directory", result);
    }

    #endregion

    #region Security Tests

    [Fact]
    public async Task ReadFile_PreventsPathTraversal()
    {
        var result = await CallReadFileAsync(
            Path.Combine(_testDirectory, "..", "..", "etc", "passwd"),
            allowedDir: _testDirectory);

        Assert.Contains("Error", result);
        Assert.Contains("outside allowed directory", result);
    }

    [Fact]
    public async Task WriteFile_PreventsPathTraversal()
    {
        var result = await CallWriteFileAsync(
            Path.Combine(_testDirectory, "..", "malicious.txt"),
            "malicious",
            _testDirectory);

        Assert.Contains("Error", result);
        Assert.Contains("outside allowed directory", result);
    }

    #endregion
}
