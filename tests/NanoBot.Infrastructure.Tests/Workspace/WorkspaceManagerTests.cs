using NanoBot.Core.Configuration;
using NanoBot.Infrastructure.Resources;
using NanoBot.Infrastructure.Workspace;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Workspace;

public class WorkspaceManagerTests
{
    private static string CreateUniqueTestPath(string testName)
    {
        return Path.Combine(Path.GetTempPath(), $"nanobot_{testName}_{Guid.NewGuid():N}");
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
    public void GetWorkspacePath_ReturnsResolvedPath()
    {
        var testPath = CreateUniqueTestPath("path_test");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        var result = workspaceManager.GetWorkspacePath();

        Assert.Equal(testPath, result);
    }

    [Fact]
    public void GetWorkspacePath_ExpandsTildePath()
    {
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = new WorkspaceConfig { Path = "~/test_workspace" };
        var manager = new WorkspaceManager(config);

        var result = manager.GetWorkspacePath();

        Assert.StartsWith(homePath, result);
        Assert.Contains("test_workspace", result);
    }

    [Fact]
    public void GetMemoryPath_ReturnsCorrectPath()
    {
        var testPath = CreateUniqueTestPath("memory_path");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        var result = workspaceManager.GetMemoryPath();

        Assert.Equal(Path.Combine(testPath, "memory"), result);
    }

    [Fact]
    public void GetSkillsPath_ReturnsCorrectPath()
    {
        var testPath = CreateUniqueTestPath("skills_path");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        var result = workspaceManager.GetSkillsPath();

        Assert.Equal(Path.Combine(testPath, "skills"), result);
    }

    [Fact]
    public void GetSessionsPath_ReturnsCorrectPath()
    {
        var testPath = CreateUniqueTestPath("sessions_path");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        var result = workspaceManager.GetSessionsPath();

        Assert.Equal(Path.Combine(testPath, "sessions"), result);
    }

    [Fact]
    public void GetAgentsFile_ReturnsCorrectPath()
    {
        var testPath = CreateUniqueTestPath("agents_file");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        var result = workspaceManager.GetAgentsFile();

        Assert.Equal(Path.Combine(testPath, "AGENTS.md"), result);
    }

    [Fact]
    public async Task InitializeAsync_CreatesDirectoryStructure()
    {
        var testPath = CreateUniqueTestPath("init_dirs");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            await workspaceManager.InitializeAsync();

            Assert.True(Directory.Exists(testPath));
            Assert.True(Directory.Exists(workspaceManager.GetMemoryPath()));
            Assert.True(Directory.Exists(workspaceManager.GetSkillsPath()));
            Assert.True(Directory.Exists(workspaceManager.GetSessionsPath()));
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesDefaultFiles()
    {
        var testPath = CreateUniqueTestPath("init_files");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            await workspaceManager.InitializeAsync();

            Assert.True(File.Exists(workspaceManager.GetAgentsFile()));
            Assert.True(File.Exists(workspaceManager.GetSoulFile()));
            Assert.True(File.Exists(workspaceManager.GetToolsFile()));
            Assert.True(File.Exists(workspaceManager.GetUserFile()));
            Assert.True(File.Exists(workspaceManager.GetHeartbeatFile()));
            Assert.True(File.Exists(workspaceManager.GetMemoryFile()));
            Assert.True(File.Exists(workspaceManager.GetHistoryFile()));
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_DoesNotOverwriteExistingFiles()
    {
        var testPath = CreateUniqueTestPath("no_overwrite");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            await workspaceManager.InitializeAsync();
            var customContent = "Custom AGENTS content";
            await File.WriteAllTextAsync(workspaceManager.GetAgentsFile(), customContent);

            await workspaceManager.InitializeAsync();

            var content = await File.ReadAllTextAsync(workspaceManager.GetAgentsFile());
            Assert.Equal(customContent, content);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        var testPath = CreateUniqueTestPath("idempotent");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            await workspaceManager.InitializeAsync();
            await workspaceManager.InitializeAsync();

            Assert.True(Directory.Exists(testPath));
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public void EnsureDirectory_CreatesDirectoryIfNotExists()
    {
        var testPath = CreateUniqueTestPath("ensure_dir");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var testDir = Path.Combine(testPath, "test_dir");

        try
        {
            workspaceManager.EnsureDirectory(testDir);

            Assert.True(Directory.Exists(testDir));
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public void EnsureDirectory_DoesNotThrowIfExists()
    {
        var testPath = CreateUniqueTestPath("ensure_dir_exists");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);
        var testDir = Path.Combine(testPath, "test_dir");

        try
        {
            Directory.CreateDirectory(testPath);
            Directory.CreateDirectory(testDir);

            var exception = Record.Exception(() => workspaceManager.EnsureDirectory(testDir));

            Assert.Null(exception);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public void FileExists_ReturnsTrueForExistingFile()
    {
        var testPath = CreateUniqueTestPath("file_exists");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            Directory.CreateDirectory(testPath);
            File.WriteAllText(Path.Combine(testPath, "test.txt"), "content");

            var result = workspaceManager.FileExists("test.txt");

            Assert.True(result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public void FileExists_ReturnsFalseForNonExistingFile()
    {
        var testPath = CreateUniqueTestPath("file_not_exists");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            Directory.CreateDirectory(testPath);

            var result = workspaceManager.FileExists("nonexistent.txt");

            Assert.False(result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task ReadFileAsync_ReturnsContentForExistingFile()
    {
        var testPath = CreateUniqueTestPath("read_file");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            Directory.CreateDirectory(testPath);
            var expectedContent = "test content";
            await File.WriteAllTextAsync(Path.Combine(testPath, "test.txt"), expectedContent);

            var result = await workspaceManager.ReadFileAsync("test.txt");

            Assert.Equal(expectedContent, result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task ReadFileAsync_ReturnsNullForNonExistingFile()
    {
        var testPath = CreateUniqueTestPath("read_not_exists");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            Directory.CreateDirectory(testPath);

            var result = await workspaceManager.ReadFileAsync("nonexistent.txt");

            Assert.Null(result);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task WriteFileAsync_CreatesFileWithContent()
    {
        var testPath = CreateUniqueTestPath("write_file");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            Directory.CreateDirectory(testPath);
            var content = "test content";

            await workspaceManager.WriteFileAsync("test.txt", content);

            var filePath = Path.Combine(testPath, "test.txt");
            Assert.True(File.Exists(filePath));
            var actualContent = await File.ReadAllTextAsync(filePath);
            Assert.Equal(content, actualContent);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task WriteFileAsync_CreatesParentDirectories()
    {
        var testPath = CreateUniqueTestPath("write_subdir");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            var content = "test content";

            await workspaceManager.WriteFileAsync("subdir/test.txt", content);

            var filePath = Path.Combine(testPath, "subdir", "test.txt");
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task AppendFileAsync_AppendsContent()
    {
        var testPath = CreateUniqueTestPath("append_file");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            Directory.CreateDirectory(testPath);
            var filePath = Path.Combine(testPath, "test.txt");
            await File.WriteAllTextAsync(filePath, "initial ");

            await workspaceManager.AppendFileAsync("test.txt", "appended");

            var content = await File.ReadAllTextAsync(filePath);
            Assert.Equal("initial appended", content);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task AppendFileAsync_CreatesFileIfNotExists()
    {
        var testPath = CreateUniqueTestPath("append_create");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            Directory.CreateDirectory(testPath);
            var filePath = Path.Combine(testPath, "test.txt");

            await workspaceManager.AppendFileAsync("test.txt", "content");

            Assert.True(File.Exists(filePath));
            var content = await File.ReadAllTextAsync(filePath);
            Assert.Equal("content", content);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task AppendFileAsync_CreatesParentDirectories()
    {
        var testPath = CreateUniqueTestPath("append_subdir");
        var config = new WorkspaceConfig { Path = testPath };
        var workspaceManager = new WorkspaceManager(config);

        try
        {
            var content = "test content";

            await workspaceManager.AppendFileAsync("subdir/test.txt", content);

            var filePath = Path.Combine(testPath, "subdir", "test.txt");
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }
}

public class WorkspaceManagerWithResourcesTests
{
    private static string CreateUniqueTestPath(string testName)
    {
        return Path.Combine(Path.GetTempPath(), $"nanobot_res_{testName}_{Guid.NewGuid():N}");
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
    public async Task InitializeAsync_WithResourceLoader_ExtractsWorkspaceResources()
    {
        var testPath = CreateUniqueTestPath("extract_workspace");
        var config = new WorkspaceConfig { Path = testPath };
        var resourceLoader = new EmbeddedResourceLoader();
        var manager = new WorkspaceManager(config, resourceLoader);

        try
        {
            await manager.InitializeAsync();

            Assert.True(File.Exists(manager.GetAgentsFile()));
            Assert.True(File.Exists(manager.GetSoulFile()));
            Assert.True(File.Exists(manager.GetToolsFile()));
            Assert.True(File.Exists(manager.GetUserFile()));
            Assert.True(File.Exists(manager.GetHeartbeatFile()));
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_WithResourceLoader_ExtractsSkillsResources()
    {
        var testPath = CreateUniqueTestPath("extract_skills");
        var config = new WorkspaceConfig { Path = testPath };
        var resourceLoader = new EmbeddedResourceLoader();
        var manager = new WorkspaceManager(config, resourceLoader);

        try
        {
            await manager.InitializeAsync();

            var skillsPath = manager.GetSkillsPath();
            Assert.True(Directory.Exists(skillsPath));

            var skillFiles = Directory.GetFiles(skillsPath, "SKILL.md", SearchOption.AllDirectories);
            Assert.NotEmpty(skillFiles);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_WithResourceLoader_ExtractsMemorySkill()
    {
        var testPath = CreateUniqueTestPath("extract_memory");
        var config = new WorkspaceConfig { Path = testPath };
        var resourceLoader = new EmbeddedResourceLoader();
        var manager = new WorkspaceManager(config, resourceLoader);

        try
        {
            await manager.InitializeAsync();

            var memorySkillPath = Path.Combine(manager.GetSkillsPath(), "memory", "SKILL.md");
            Assert.True(File.Exists(memorySkillPath));

            var content = await File.ReadAllTextAsync(memorySkillPath);
            Assert.Contains("Memory", content);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_WithResourceLoader_DoesNotOverwriteExistingFiles()
    {
        var testPath = CreateUniqueTestPath("no_overwrite_res");
        var config = new WorkspaceConfig { Path = testPath };
        var resourceLoader = new EmbeddedResourceLoader();
        var manager = new WorkspaceManager(config, resourceLoader);

        try
        {
            Directory.CreateDirectory(testPath);
            var customContent = "Custom AGENTS content";
            await File.WriteAllTextAsync(manager.GetAgentsFile(), customContent);

            await manager.InitializeAsync();

            var content = await File.ReadAllTextAsync(manager.GetAgentsFile());
            Assert.Equal(customContent, content);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_WithResourceLoader_UsesEmbeddedContent()
    {
        var testPath = CreateUniqueTestPath("embedded_content");
        var config = new WorkspaceConfig { Path = testPath };
        var resourceLoader = new EmbeddedResourceLoader();
        var manager = new WorkspaceManager(config, resourceLoader);

        try
        {
            await manager.InitializeAsync();

            var agentsContent = await File.ReadAllTextAsync(manager.GetAgentsFile());
            Assert.Contains("Agent Instructions", agentsContent);

            var soulContent = await File.ReadAllTextAsync(manager.GetSoulFile());
            Assert.Contains("Soul", soulContent);
        }
        finally
        {
            CleanupTestPath(testPath);
        }
    }
}
