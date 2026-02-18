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

    public SkillsContextProvider(
        ISkillsLoader skillsLoader,
        ILogger<SkillsContextProvider>? logger = null)
    {
        _skillsLoader = skillsLoader ?? throw new ArgumentNullException(nameof(skillsLoader));
        _logger = logger;
    }

    public override JsonElement Serialize(JsonSerializerOptions? options = null)
    {
        return JsonDocument.Parse("{}").RootElement.Clone();
    }

    protected override async ValueTask<AIContext> InvokingCoreAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var alwaysSkills = _skillsLoader.GetAlwaysSkills();
            var skillsSummary = await _skillsLoader.BuildSkillsSummaryAsync(cancellationToken);

            if (alwaysSkills.Count == 0 && string.IsNullOrEmpty(skillsSummary))
            {
                return new AIContext();
            }

            var instructions = new StringBuilder();

            if (alwaysSkills.Count > 0)
            {
                var alwaysContent = await _skillsLoader.LoadSkillsForContextAsync(alwaysSkills, cancellationToken);
                if (!string.IsNullOrEmpty(alwaysContent))
                {
                    instructions.AppendLine("# Active Skills");
                    instructions.AppendLine();
                    instructions.AppendLine(alwaysContent);
                    instructions.AppendLine();
                }
            }

            if (!string.IsNullOrEmpty(skillsSummary))
            {
                instructions.AppendLine("# Skills");
                instructions.AppendLine();
                instructions.AppendLine("The following skills extend your capabilities. To use a skill, read its SKILL.md file using the read_file tool.");
                instructions.AppendLine("Skills with available=\"false\" need dependencies installed first - you can try installing them with apt/brew.");
                instructions.AppendLine();
                instructions.AppendLine(skillsSummary);
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
}
