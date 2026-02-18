using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Skills;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Resources;

namespace NanoBot.Infrastructure.Skills;

public class SkillsLoader : ISkillsLoader
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IEmbeddedResourceLoader _resourceLoader;
    private readonly ILogger<SkillsLoader> _logger;

    private List<Skill> _loadedSkills = new();
    private readonly Dictionary<string, SkillMetadata> _metadataCache = new();

    public event EventHandler<SkillsChangedEventArgs>? SkillsChanged;

    public SkillsLoader(
        IWorkspaceManager workspaceManager,
        IEmbeddedResourceLoader resourceLoader,
        ILogger<SkillsLoader> logger)
    {
        _workspaceManager = workspaceManager;
        _resourceLoader = resourceLoader;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Skill>> LoadAsync(string directory, CancellationToken cancellationToken = default)
    {
        var skills = new List<Skill>();
        var added = new List<Skill>();

        var workspaceSkillsPath = _workspaceManager.GetSkillsPath();
        if (Directory.Exists(workspaceSkillsPath))
        {
            foreach (var skillDir in Directory.GetDirectories(workspaceSkillsPath))
            {
                var skill = await LoadSkillFromDirectoryAsync(skillDir, "workspace", cancellationToken);
                if (skill != null)
                {
                    skills.Add(skill);
                    added.Add(skill);
                }
            }
        }

        var embeddedSkills = _resourceLoader.GetSkillsResourceNames()
            .Where(n => n.EndsWith("/SKILL.md"))
            .Select(n => n.Split('/')[1])
            .Distinct();

        foreach (var skillName in embeddedSkills)
        {
            if (skills.Any(s => s.Name == skillName)) continue;

            var skill = await LoadSkillFromEmbeddedAsync(skillName, cancellationToken);
            if (skill != null)
            {
                skills.Add(skill);
                added.Add(skill);
            }
        }

        var removed = _loadedSkills.Where(s => !skills.Any(ns => ns.Name == s.Name)).ToList();
        _loadedSkills = skills;

        if (added.Count > 0 || removed.Count > 0)
        {
            SkillsChanged?.Invoke(this, new SkillsChangedEventArgs
            {
                Added = added,
                Removed = removed
            });
        }

        _logger.LogInformation("Loaded {Count} skills", skills.Count);
        return skills.AsReadOnly();
    }

    public IReadOnlyList<Skill> GetLoadedSkills()
    {
        return _loadedSkills.AsReadOnly();
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        _metadataCache.Clear();
        await LoadAsync(_workspaceManager.GetSkillsPath(), cancellationToken);
    }

    public IReadOnlyList<SkillSummary> ListSkills(bool filterUnavailable = true)
    {
        var summaries = new List<SkillSummary>();

        foreach (var skill in _loadedSkills)
        {
            var metadata = _metadataCache.GetValueOrDefault(skill.Name);
            var available = metadata == null || CheckRequirements(metadata);

            if (filterUnavailable && !available) continue;

            summaries.Add(new SkillSummary
            {
                Name = skill.Name,
                Description = skill.Description,
                FilePath = skill.FilePath ?? string.Empty,
                Source = skill.Source,
                Available = available,
                MissingRequirements = available ? null : GetMissingRequirements(metadata!)
            });
        }

        return summaries.AsReadOnly();
    }

    public async Task<Skill?> LoadSkillAsync(string name, CancellationToken cancellationToken = default)
    {
        var existing = _loadedSkills.FirstOrDefault(s => s.Name == name);
        if (existing != null) return existing;

        var workspaceSkillPath = Path.Combine(_workspaceManager.GetSkillsPath(), name, "SKILL.md");
        if (File.Exists(workspaceSkillPath))
        {
            return await LoadSkillFromFileAsync(workspaceSkillPath, "workspace", cancellationToken);
        }

        return await LoadSkillFromEmbeddedAsync(name, cancellationToken);
    }

    public async Task<string> LoadSkillsForContextAsync(IReadOnlyList<string> skillNames, CancellationToken cancellationToken = default)
    {
        var parts = new List<string>();

        foreach (var name in skillNames)
        {
            var skill = await LoadSkillAsync(name, cancellationToken);
            if (skill?.Content != null)
            {
                var content = StripFrontmatter(skill.Content);
                parts.Add($"### Skill: {name}\n\n{content}");
            }
        }

        return parts.Count > 0 ? string.Join("\n\n---\n\n", parts) : string.Empty;
    }

    public async Task<string> BuildSkillsSummaryAsync(CancellationToken cancellationToken = default)
    {
        var allSkills = ListSkills(filterUnavailable: false);
        if (allSkills.Count == 0) return string.Empty;

        var lines = new List<string> { "<skills>" };

        foreach (var skill in allSkills)
        {
            var escapedName = EscapeXml(skill.Name);
            var escapedDesc = EscapeXml(skill.Description);
            var escapedPath = EscapeXml(skill.FilePath);

            lines.Add($"  <skill available=\"{skill.Available.ToString().ToLowerInvariant()}\">");
            lines.Add($"    <name>{escapedName}</name>");
            lines.Add($"    <description>{escapedDesc}</description>");
            lines.Add($"    <location>{escapedPath}</location>");

            if (!skill.Available && skill.MissingRequirements != null)
            {
                lines.Add($"    <requires>{EscapeXml(skill.MissingRequirements)}</requires>");
            }

            lines.Add("  </skill>");
        }

        lines.Add("</skills>");
        return string.Join("\n", lines);
    }

    public IReadOnlyList<string> GetAlwaysSkills()
    {
        var result = new List<string>();

        foreach (var skill in ListSkills(filterUnavailable: true))
        {
            var metadata = _metadataCache.GetValueOrDefault(skill.Name);
            if (metadata?.Always == true || metadata?.Nanobot?.Requires == null)
            {
                result.Add(skill.Name);
            }
        }

        return result.AsReadOnly();
    }

    public async Task<SkillMetadata?> GetSkillMetadataAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_metadataCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var skill = await LoadSkillAsync(name, cancellationToken);
        if (skill?.Content == null) return null;

        var metadata = ParseFrontmatter(skill.Content);
        if (metadata != null)
        {
            _metadataCache[name] = metadata;
        }

        return metadata;
    }

    public bool CheckRequirements(SkillMetadata metadata)
    {
        var requires = metadata.Nanobot?.Requires;
        if (requires == null) return true;

        foreach (var bin in requires.Bins ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrEmpty(FindExecutable(bin)))
            {
                return false;
            }
        }

        foreach (var env in requires.Env ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(env)))
            {
                return false;
            }
        }

        return true;
    }

    public string? GetMissingRequirements(SkillMetadata metadata)
    {
        var missing = new List<string>();
        var requires = metadata.Nanobot?.Requires;

        if (requires != null)
        {
            foreach (var bin in requires.Bins ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(FindExecutable(bin)))
                {
                    missing.Add($"CLI: {bin}");
                }
            }

            foreach (var env in requires.Env ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(env)))
                {
                    missing.Add($"ENV: {env}");
                }
            }
        }

        return missing.Count > 0 ? string.Join(", ", missing) : null;
    }

    private async Task<Skill?> LoadSkillFromDirectoryAsync(string directory, string source, CancellationToken cancellationToken)
    {
        var skillFile = Path.Combine(directory, "SKILL.md");
        if (!File.Exists(skillFile)) return null;

        return await LoadSkillFromFileAsync(skillFile, source, cancellationToken);
    }

    private async Task<Skill?> LoadSkillFromFileAsync(string filePath, string source, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var name = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? Path.GetFileNameWithoutExtension(filePath);
            var metadata = ParseFrontmatter(content);

            if (metadata != null)
            {
                _metadataCache[metadata.Name] = metadata;
            }

            return new Skill
            {
                Name = metadata?.Name ?? name,
                Description = metadata?.Description ?? name,
                Content = content,
                FilePath = filePath,
                Source = source,
                LoadedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load skill from {FilePath}", filePath);
            return null;
        }
    }

    private async Task<Skill?> LoadSkillFromEmbeddedAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var resourceName = $"skills/{name}/SKILL.md";
            var content = await _resourceLoader.ReadResourceAsync(resourceName, cancellationToken);

            if (content == null) return null;

            var metadata = ParseFrontmatter(content);

            if (metadata != null)
            {
                _metadataCache[metadata.Name] = metadata;
            }

            return new Skill
            {
                Name = metadata?.Name ?? name,
                Description = metadata?.Description ?? name,
                Content = content,
                FilePath = $"embedded:{resourceName}",
                Source = "builtin",
                LoadedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load embedded skill {SkillName}", name);
            return null;
        }
    }

    private SkillMetadata? ParseFrontmatter(string content)
    {
        if (!content.StartsWith("---")) return null;

        var match = Regex.Match(content, @"^---\n(.*?)\n---", RegexOptions.Singleline);
        if (!match.Success) return null;

        var frontmatter = match.Groups[1].Value;
        var metadata = new Dictionary<string, string>();

        foreach (var line in frontmatter.Split('\n'))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim().Trim('"').Trim('\'');
                metadata[key] = value;
            }
        }

        if (!metadata.TryGetValue("name", out var name) || !metadata.TryGetValue("description", out var description))
        {
            return null;
        }

        NanobotMetadata? nanobotMeta = null;
        if (metadata.TryGetValue("metadata", out var metadataJson))
        {
            nanobotMeta = ParseNanobotMetadata(metadataJson);
        }

        return new SkillMetadata
        {
            Name = name,
            Description = description,
            Homepage = metadata.GetValueOrDefault("homepage"),
            Always = metadata.TryGetValue("always", out var always) && bool.TryParse(always, out var a) && a,
            Nanobot = nanobotMeta
        };
    }

    private NanobotMetadata? ParseNanobotMetadata(string json)
    {
        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            var nanobot = data.TryGetProperty("nanobot", out var n) ? n :
                          data.TryGetProperty("openclaw", out var oc) ? oc :
                          default;

            if (nanobot.ValueKind == JsonValueKind.Undefined) return null;

            RequirementsMetadata? requires = null;
            if (nanobot.TryGetProperty("requires", out var req))
            {
                requires = new RequirementsMetadata
                {
                    Bins = req.TryGetProperty("bins", out var bins)
                        ? bins.EnumerateArray().Select(b => b.GetString()).Where(b => b != null).Cast<string>().ToList()
                        : null,
                    Env = req.TryGetProperty("env", out var env)
                        ? env.EnumerateArray().Select(e => e.GetString()).Where(e => e != null).Cast<string>().ToList()
                        : null
                };
            }

            List<InstallMetadata>? install = null;
            if (nanobot.TryGetProperty("install", out var inst))
            {
                install = inst.EnumerateArray()
                    .Select(i => new InstallMetadata
                    {
                        Id = i.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                        Kind = i.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "" : "",
                        Formula = i.TryGetProperty("formula", out var formula) ? formula.GetString() ?? "" : "",
                        Bins = i.TryGetProperty("bins", out var ibins)
                            ? ibins.EnumerateArray().Select(b => b.GetString()).Where(b => b != null).Cast<string>().ToList()
                            : null,
                        Label = i.TryGetProperty("label", out var label) ? label.GetString() ?? "" : ""
                    })
                    .ToList();
            }

            return new NanobotMetadata
            {
                Emoji = nanobot.TryGetProperty("emoji", out var emoji) ? emoji.GetString() : null,
                Requires = requires,
                Install = install
            };
        }
        catch
        {
            return null;
        }
    }

    private static string StripFrontmatter(string content)
    {
        if (!content.StartsWith("---")) return content;

        var match = Regex.Match(content, @"^---\n.*?\n---\n", RegexOptions.Singleline);
        return match.Success ? content[match.Length..].Trim() : content;
    }

    private static string EscapeXml(string s)
    {
        return s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static string? FindExecutable(string name)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        var extensions = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        foreach (var path in paths)
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(path, name + ext);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }
}
