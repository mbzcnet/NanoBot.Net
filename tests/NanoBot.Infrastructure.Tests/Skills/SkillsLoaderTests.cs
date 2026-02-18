using Microsoft.Extensions.Logging;
using Moq;
using NanoBot.Core.Skills;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Resources;
using NanoBot.Infrastructure.Skills;
using Xunit;

namespace NanoBot.Infrastructure.Tests.Skills;

public class SkillsLoaderTests
{
    private readonly Mock<IWorkspaceManager> _workspaceManagerMock;
    private readonly Mock<IEmbeddedResourceLoader> _resourceLoaderMock;
    private readonly ILogger<SkillsLoader> _logger;

    public SkillsLoaderTests()
    {
        _workspaceManagerMock = new Mock<IWorkspaceManager>();
        _resourceLoaderMock = new Mock<IEmbeddedResourceLoader>();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _logger = loggerFactory.CreateLogger<SkillsLoader>();
    }

    [Fact]
    public void Constructor_CreatesInstance()
    {
        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        Assert.NotNull(loader);
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmptyList_WhenNoSkills()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"skills_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);

        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns(tempPath);
        _resourceLoaderMock.Setup(x => x.GetSkillsResourceNames()).Returns(Array.Empty<string>());

        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        var skills = await loader.LoadAsync(tempPath);

        Assert.Empty(skills);

        Directory.Delete(tempPath, true);
    }

    [Fact]
    public async Task LoadAsync_LoadsSkillFromFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"skills_test_{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tempPath, "test-skill");
        Directory.CreateDirectory(skillDir);

        var skillContent = "---\nname: test-skill\ndescription: A test skill\n---\n\n# Test Skill\n\nThis is a test skill content.\n";
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), skillContent);

        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns(tempPath);
        _resourceLoaderMock.Setup(x => x.GetSkillsResourceNames()).Returns(Array.Empty<string>());

        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        var skills = await loader.LoadAsync(tempPath);

        Assert.Single(skills);
        Assert.Equal("test-skill", skills[0].Name);
        Assert.Equal("A test skill", skills[0].Description);
        Assert.Equal("workspace", skills[0].Source);

        Directory.Delete(tempPath, true);
    }

    [Fact]
    public async Task LoadAsync_LoadsEmbeddedSkill()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"skills_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);

        var embeddedContent = "---\nname: embedded-skill\ndescription: An embedded skill\n---\n\n# Embedded Skill\n\nThis is embedded content.\n";

        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns(tempPath);
        _resourceLoaderMock.Setup(x => x.GetSkillsResourceNames())
            .Returns(new[] { "skills/embedded-skill/SKILL.md" });
        _resourceLoaderMock.Setup(x => x.ReadResourceAsync("skills/embedded-skill/SKILL.md", default))
            .ReturnsAsync(embeddedContent);

        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        var skills = await loader.LoadAsync(tempPath);

        Assert.Single(skills);
        Assert.Equal("embedded-skill", skills[0].Name);
        Assert.Equal("builtin", skills[0].Source);

        Directory.Delete(tempPath, true);
    }

    [Fact]
    public async Task GetLoadedSkills_ReturnsCachedSkills()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"skills_test_{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tempPath, "cached-skill");
        Directory.CreateDirectory(skillDir);

        var skillContent = "---\nname: cached-skill\ndescription: A cached skill\n---\nContent\n";
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), skillContent);

        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns(tempPath);
        _resourceLoaderMock.Setup(x => x.GetSkillsResourceNames()).Returns(Array.Empty<string>());

        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        await loader.LoadAsync(tempPath);
        var cachedSkills = loader.GetLoadedSkills();

        Assert.Single(cachedSkills);
        Assert.Equal("cached-skill", cachedSkills[0].Name);

        Directory.Delete(tempPath, true);
    }

    [Fact]
    public async Task ListSkills_FiltersUnavailable()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"skills_test_{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tempPath, "available-skill");
        Directory.CreateDirectory(skillDir);

        var skillContent = "---\nname: available-skill\ndescription: An available skill\n---\nContent\n";
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), skillContent);

        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns(tempPath);
        _resourceLoaderMock.Setup(x => x.GetSkillsResourceNames()).Returns(Array.Empty<string>());

        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        await loader.LoadAsync(tempPath);
        var summaries = loader.ListSkills(filterUnavailable: true);

        Assert.Single(summaries);
        Assert.True(summaries[0].Available);

        Directory.Delete(tempPath, true);
    }

    [Fact]
    public async Task BuildSkillsSummary_ReturnsXmlFormat()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"skills_test_{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tempPath, "summary-skill");
        Directory.CreateDirectory(skillDir);

        var skillContent = "---\nname: summary-skill\ndescription: A skill for summary\n---\nContent\n";
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), skillContent);

        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns(tempPath);
        _resourceLoaderMock.Setup(x => x.GetSkillsResourceNames()).Returns(Array.Empty<string>());

        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        await loader.LoadAsync(tempPath);
        var summary = await loader.BuildSkillsSummaryAsync();

        Assert.Contains("<skills>", summary);
        Assert.Contains("<name>summary-skill</name>", summary);
        Assert.Contains("<description>A skill for summary</description>", summary);
        Assert.Contains("</skills>", summary);

        Directory.Delete(tempPath, true);
    }

    [Fact]
    public async Task LoadSkillsForContext_ReturnsFormattedContent()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"skills_test_{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tempPath, "context-skill");
        Directory.CreateDirectory(skillDir);

        var skillContent = "---\nname: context-skill\ndescription: A skill for context\n---\n\n# Context Skill\n\nInstructions here.\n";
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), skillContent);

        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns(tempPath);
        _resourceLoaderMock.Setup(x => x.GetSkillsResourceNames()).Returns(Array.Empty<string>());

        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        await loader.LoadAsync(tempPath);
        var context = await loader.LoadSkillsForContextAsync(new[] { "context-skill" });

        Assert.Contains("### Skill: context-skill", context);
        Assert.Contains("# Context Skill", context);
        Assert.DoesNotContain("---", context);

        Directory.Delete(tempPath, true);
    }

    [Fact]
    public async Task GetSkillMetadata_ReturnsParsedMetadata()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"skills_test_{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tempPath, "meta-skill");
        Directory.CreateDirectory(skillDir);

        var skillContent = "---\nname: meta-skill\ndescription: A skill with metadata\nhomepage: https://example.com\nalways: true\n---\nContent\n";
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), skillContent);

        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns(tempPath);
        _resourceLoaderMock.Setup(x => x.GetSkillsResourceNames()).Returns(Array.Empty<string>());

        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        await loader.LoadAsync(tempPath);
        var metadata = await loader.GetSkillMetadataAsync("meta-skill");

        Assert.NotNull(metadata);
        Assert.Equal("meta-skill", metadata.Name);
        Assert.Equal("A skill with metadata", metadata.Description);
        Assert.Equal("https://example.com", metadata.Homepage);
        Assert.True(metadata.Always);

        Directory.Delete(tempPath, true);
    }

    [Fact]
    public void CheckRequirements_ReturnsTrue_WhenNoRequirements()
    {
        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        var metadata = new SkillMetadata
        {
            Name = "test",
            Description = "test"
        };

        var result = loader.CheckRequirements(metadata);
        Assert.True(result);
    }

    [Fact]
    public void CheckRequirements_ReturnsFalse_WhenBinNotFound()
    {
        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        var metadata = new SkillMetadata
        {
            Name = "test",
            Description = "test",
            Nanobot = new NanobotMetadata
            {
                Requires = new RequirementsMetadata
                {
                    Bins = new List<string> { "nonexistent-cli-tool-xyz" }
                }
            }
        };

        var result = loader.CheckRequirements(metadata);
        Assert.False(result);
    }

    [Fact]
    public void GetMissingRequirements_ReturnsMissingItems()
    {
        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        var metadata = new SkillMetadata
        {
            Name = "test",
            Description = "test",
            Nanobot = new NanobotMetadata
            {
                Requires = new RequirementsMetadata
                {
                    Bins = new List<string> { "nonexistent-cli-tool-xyz" },
                    Env = new List<string> { "NONEXISTENT_ENV_VAR_XYZ" }
                }
            }
        };

        var missing = loader.GetMissingRequirements(metadata);

        Assert.NotNull(missing);
        Assert.Contains("CLI: nonexistent-cli-tool-xyz", missing);
        Assert.Contains("ENV: NONEXISTENT_ENV_VAR_XYZ", missing);
    }

    [Fact]
    public async Task SkillsChanged_EventIsRaised()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"skills_test_{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tempPath, "event-skill");
        Directory.CreateDirectory(skillDir);

        var skillContent = "---\nname: event-skill\ndescription: Event test\n---\nContent\n";
        await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"), skillContent);

        _workspaceManagerMock.Setup(x => x.GetSkillsPath()).Returns(tempPath);
        _resourceLoaderMock.Setup(x => x.GetSkillsResourceNames()).Returns(Array.Empty<string>());

        var loader = new SkillsLoader(
            _workspaceManagerMock.Object,
            _resourceLoaderMock.Object,
            _logger);

        SkillsChangedEventArgs? eventArgs = null;
        loader.SkillsChanged += (sender, args) => eventArgs = args;

        await loader.LoadAsync(tempPath);

        Assert.NotNull(eventArgs);
        Assert.Single(eventArgs.Added);
        Assert.Equal("event-skill", eventArgs.Added[0].Name);

        Directory.Delete(tempPath, true);
    }
}
