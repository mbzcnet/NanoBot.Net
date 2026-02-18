using NanoBot.Core.Configuration;
using NanoBot.Infrastructure.Workspace;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Workspace;

public class BootstrapLoaderTests
{
    private static string CreateUniqueTestPath(string testName)
    {
        return Path.Combine(Path.GetTempPath(), $"nanobot_bootstrap_{testName}_{Guid.NewGuid():N}");
    }

    private static void CleanupTestPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void BootstrapFiles_ReturnsExpectedFiles()
    {
        var testPath = CreateUniqueTestPath("files_list");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        var files = bootstrapLoader.BootstrapFiles;

        Assert.Equal(4, files.Count);
        Assert.Contains("AGENTS.md", files);
        Assert.Contains("SOUL.md", files);
        Assert.Contains("USER.md", files);
        Assert.Contains("TOOLS.md", files);
    }

    [Fact]
    public async Task LoadBootstrapFileAsync_ReturnsNullForNonExistingFile()
    {
        var testPath = CreateUniqueTestPath("null_nonexist");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        var result = await bootstrapLoader.LoadBootstrapFileAsync("NONEXISTENT.md");

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadBootstrapFileAsync_ReturnsContentForExistingFile()
    {
        var testPath = CreateUniqueTestPath("content_exist");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        try
        {
            await workspaceManager.InitializeAsync();

            var result = await bootstrapLoader.LoadBootstrapFileAsync("AGENTS.md");

            Assert.NotNull(result);
            Assert.Contains("Agent Instructions", result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task LoadAgentsAsync_ReturnsContent()
    {
        var testPath = CreateUniqueTestPath("load_agents");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        try
        {
            await workspaceManager.InitializeAsync();

            var result = await bootstrapLoader.LoadAgentsAsync();

            Assert.NotNull(result);
            Assert.Contains("Agent Instructions", result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task LoadSoulAsync_ReturnsContent()
    {
        var testPath = CreateUniqueTestPath("load_soul");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        try
        {
            await workspaceManager.InitializeAsync();

            var result = await bootstrapLoader.LoadSoulAsync();

            Assert.NotNull(result);
            Assert.Contains("Soul", result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task LoadToolsAsync_ReturnsContent()
    {
        var testPath = CreateUniqueTestPath("load_tools");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        try
        {
            await workspaceManager.InitializeAsync();

            var result = await bootstrapLoader.LoadToolsAsync();

            Assert.NotNull(result);
            Assert.Contains("Available Tools", result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task LoadUserAsync_ReturnsContent()
    {
        var testPath = CreateUniqueTestPath("load_user");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        try
        {
            await workspaceManager.InitializeAsync();

            var result = await bootstrapLoader.LoadUserAsync();

            Assert.NotNull(result);
            Assert.Contains("User Profile", result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task LoadHeartbeatAsync_ReturnsContent()
    {
        var testPath = CreateUniqueTestPath("load_heartbeat");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        try
        {
            await workspaceManager.InitializeAsync();

            var result = await bootstrapLoader.LoadHeartbeatAsync();

            Assert.NotNull(result);
            Assert.Contains("Heartbeat Tasks", result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task LoadAllBootstrapFilesAsync_ReturnsCombinedContent()
    {
        var testPath = CreateUniqueTestPath("load_all");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        try
        {
            await workspaceManager.InitializeAsync();

            var result = await bootstrapLoader.LoadAllBootstrapFilesAsync();

            Assert.NotNull(result);
            Assert.Contains("## AGENTS.md", result);
            Assert.Contains("## SOUL.md", result);
            Assert.Contains("## USER.md", result);
            Assert.Contains("## TOOLS.md", result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task LoadAllBootstrapFilesAsync_ReturnsEmptyStringWhenNoFilesExist()
    {
        var testPath = CreateUniqueTestPath("empty_no_files");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        try
        {
            Directory.CreateDirectory(testPath);

            var result = await bootstrapLoader.LoadAllBootstrapFilesAsync();

            Assert.Equal(string.Empty, result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task LoadAllBootstrapFilesAsync_SkipsEmptyFiles()
    {
        var testPath = CreateUniqueTestPath("skip_empty");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        try
        {
            Directory.CreateDirectory(testPath);
            await File.WriteAllTextAsync(workspaceManager.GetAgentsFile(), "content");
            await File.WriteAllTextAsync(workspaceManager.GetSoulFile(), "");

            var result = await bootstrapLoader.LoadAllBootstrapFilesAsync();

            Assert.Contains("## AGENTS.md", result);
            Assert.DoesNotContain("## SOUL.md", result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task LoadBootstrapFileAsync_LoadsCustomFile()
    {
        var testPath = CreateUniqueTestPath("custom_file");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var bootstrapLoader = new BootstrapLoader(workspaceManager);

        try
        {
            Directory.CreateDirectory(testPath);
            var customContent = "Custom file content";
            await File.WriteAllTextAsync(Path.Combine(testPath, "CUSTOM.md"), customContent);

            var result = await bootstrapLoader.LoadBootstrapFileAsync("CUSTOM.md");

            Assert.Equal(customContent, result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }
}
