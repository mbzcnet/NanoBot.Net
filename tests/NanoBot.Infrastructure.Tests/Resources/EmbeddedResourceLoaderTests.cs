using NanoBot.Infrastructure.Resources;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Resources;

public class EmbeddedResourceLoaderTests
{
    private static string CreateUniqueTestPath(string testName)
    {
        return Path.Combine(Path.GetTempPath(), $"nanobot_res_loader_{testName}_{Guid.NewGuid():N}");
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

    private readonly EmbeddedResourceLoader _loader;

    public EmbeddedResourceLoaderTests()
    {
        _loader = new EmbeddedResourceLoader();
    }

    [Fact]
    public void GetWorkspaceResourceNames_ReturnsWorkspaceResources()
    {
        var resources = _loader.GetWorkspaceResourceNames();

        Assert.NotEmpty(resources);
        Assert.All(resources, r => Assert.StartsWith("templates/", r));
    }

    [Fact]
    public void GetWorkspaceResourceNames_ContainsExpectedFiles()
    {
        var resources = _loader.GetWorkspaceResourceNames();

        Assert.Contains(resources, r => r.Contains("AGENTS"));
        Assert.Contains(resources, r => r.Contains("SOUL"));
        Assert.Contains(resources, r => r.Contains("TOOLS"));
        Assert.Contains(resources, r => r.Contains("USER"));
        Assert.Contains(resources, r => r.Contains("HEARTBEAT"));
    }

    [Fact]
    public void GetSkillsResourceNames_ReturnsSkillsResources()
    {
        var resources = _loader.GetSkillsResourceNames();

        Assert.NotEmpty(resources);
        Assert.All(resources, r => Assert.StartsWith("skills/", r));
    }

    [Fact]
    public void GetSkillsResourceNames_ContainsExpectedSkills()
    {
        var resources = _loader.GetSkillsResourceNames();

        Assert.Contains(resources, r => r.Contains("memory"));
        Assert.Contains(resources, r => r.Contains("github"));
        Assert.Contains(resources, r => r.Contains("weather"));
        Assert.Contains(resources, r => r.Contains("summarize"));
        Assert.Contains(resources, r => r.Contains("cron"));
    }

    [Fact]
    public async Task ReadResourceAsync_ReturnsContentForExistingResource()
    {
        var resources = _loader.GetWorkspaceResourceNames();
        var firstResource = resources.First();

        var content = await _loader.ReadResourceAsync(firstResource);

        Assert.NotNull(content);
        Assert.NotEmpty(content);
    }

    [Fact]
    public async Task ReadResourceAsync_ReturnsNullForNonExistingResource()
    {
        var content = await _loader.ReadResourceAsync("nonexistent/resource.md");

        Assert.Null(content);
    }

    [Fact]
    public async Task ReadResourceAsync_ReturnsCorrectContentForAgents()
    {
        var agentsResource = _loader.GetWorkspaceResourceNames()
            .First(r => r.Contains("AGENTS"));

        var content = await _loader.ReadResourceAsync(agentsResource);

        Assert.NotNull(content);
        Assert.Contains("Agent Instructions", content);
    }

    [Fact]
    public async Task ReadResourceAsync_ReturnsCorrectContentForMemorySkill()
    {
        var memorySkillResource = _loader.GetSkillsResourceNames()
            .First(r => r.Contains("memory") && r.Contains("SKILL"));

        var content = await _loader.ReadResourceAsync(memorySkillResource);

        Assert.NotNull(content);
        Assert.Contains("Memory", content);
    }

    [Fact]
    public async Task ExtractWorkspaceResourcesAsync_ExtractsFilesToTargetDirectory()
    {
        var tempDir = CreateUniqueTestPath("extract_workspace");

        try
        {
            await _loader.ExtractWorkspaceResourcesAsync(tempDir);

            Assert.True(Directory.Exists(tempDir));

            var files = Directory.GetFiles(tempDir, "*.md", SearchOption.AllDirectories);
            Assert.NotEmpty(files);
        }
        finally
        {
            CleanupTestPath(tempDir);
        }
    }

    [Fact]
    public async Task ExtractSkillsResourcesAsync_ExtractsFilesToTargetDirectory()
    {
        var tempDir = CreateUniqueTestPath("extract_skills");

        try
        {
            await _loader.ExtractSkillsResourcesAsync(tempDir);

            Assert.True(Directory.Exists(tempDir));

            var files = Directory.GetFiles(tempDir, "SKILL.md", SearchOption.AllDirectories);
            Assert.NotEmpty(files);
        }
        finally
        {
            CleanupTestPath(tempDir);
        }
    }

    [Fact]
    public async Task ExtractWorkspaceResourcesAsync_DoesNotOverwriteExistingFiles()
    {
        var tempDir = CreateUniqueTestPath("no_overwrite");

        try
        {
            Directory.CreateDirectory(tempDir);
            var customContent = "Custom AGENTS content";
            await File.WriteAllTextAsync(Path.Combine(tempDir, "AGENTS.md"), customContent);

            await _loader.ExtractWorkspaceResourcesAsync(tempDir);

            var content = await File.ReadAllTextAsync(Path.Combine(tempDir, "AGENTS.md"));
            Assert.Equal(customContent, content);
        }
        finally
        {
            CleanupTestPath(tempDir);
        }
    }

    [Fact]
    public async Task ExtractAllResourcesAsync_ExtractsBothWorkspaceAndSkills()
    {
        var tempDir = CreateUniqueTestPath("extract_all");

        try
        {
            await _loader.ExtractAllResourcesAsync(tempDir);

            Assert.True(Directory.Exists(tempDir));

            var mdFiles = Directory.GetFiles(tempDir, "*.md", SearchOption.AllDirectories);
            Assert.NotEmpty(mdFiles);

            var skillFiles = Directory.GetFiles(tempDir, "SKILL.md", SearchOption.AllDirectories);
            Assert.NotEmpty(skillFiles);
        }
        finally
        {
            CleanupTestPath(tempDir);
        }
    }
}
