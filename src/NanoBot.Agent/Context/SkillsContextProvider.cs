using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Skills;

namespace NanoBot.Agent.Context;

public class SkillsContextProvider : AIContextProvider
{
    private readonly ISkillsLoader _skillsLoader;
    private readonly ILogger<SkillsContextProvider>? _logger;

    private string? _cachedSkillsSummary;
    private IReadOnlyList<string>? _cachedAlwaysSkills;
    private string? _cachedAlwaysSkillsContent;
    private DateTime _cacheTime;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    public SkillsContextProvider(
        ISkillsLoader skillsLoader,
        ILogger<SkillsContextProvider>? logger = null)
    {
        _skillsLoader = skillsLoader ?? throw new ArgumentNullException(nameof(skillsLoader));
        _logger = logger;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureCacheAsync(cancellationToken);

            if (_cachedAlwaysSkills.Count == 0 && string.IsNullOrEmpty(_cachedSkillsSummary))
            {
                return new AIContext();
            }

            var instructions = new StringBuilder();

            if (_cachedAlwaysSkills.Count > 0 && !string.IsNullOrEmpty(_cachedAlwaysSkillsContent))
            {
                instructions.AppendLine("# Active Skills");
                instructions.AppendLine();
                instructions.AppendLine(_cachedAlwaysSkillsContent);
                instructions.AppendLine();
            }

            if (!string.IsNullOrEmpty(_cachedSkillsSummary))
            {
                instructions.AppendLine("# Skills");
                instructions.AppendLine();
                instructions.AppendLine("The following skills extend your capabilities. To use a skill, read its SKILL.md file using the read_file tool.");
                instructions.AppendLine("Skills with available=\"false\" need dependencies installed first - you can try installing them with apt/brew.");
                instructions.AppendLine();
                instructions.AppendLine(_cachedSkillsSummary);
            }

            return new AIContext
            {
                Instructions = instructions.Length > 0 ? instructions.ToString() : null
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load skills context");
            return new AIContext();
        }
    }

    private async Task EnsureCacheAsync(CancellationToken cancellationToken)
    {
        if (_cacheTime + _cacheDuration > DateTime.UtcNow && _cachedSkillsSummary != null)
        {
            return;
        }

        _cachedAlwaysSkills = _skillsLoader.GetAlwaysSkills();
        _cachedSkillsSummary = await _skillsLoader.BuildSkillsSummaryAsync(cancellationToken);

        if (_cachedAlwaysSkills.Count > 0)
        {
            _cachedAlwaysSkillsContent = await _skillsLoader.LoadSkillsForContextAsync(_cachedAlwaysSkills, cancellationToken);
        }
        else
        {
            _cachedAlwaysSkillsContent = null;
        }

        _cacheTime = DateTime.UtcNow;
        _logger?.LogDebug("Skills context cache refreshed");
    }
}
