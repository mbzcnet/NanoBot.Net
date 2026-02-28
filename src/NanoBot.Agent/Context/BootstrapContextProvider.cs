using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent.Context;

public class BootstrapContextProvider : AIContextProvider
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<BootstrapContextProvider>? _logger;

    private static readonly string[] BootstrapFiles = ["AGENTS.md", "SOUL.md", "USER.md", "TOOLS.md"];

    private string? _cachedInstructions;
    private DateTime _cacheTime;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    public BootstrapContextProvider(
        IWorkspaceManager workspace,
        ILogger<BootstrapContextProvider>? logger = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        if (_cacheTime + _cacheDuration > DateTime.UtcNow && _cachedInstructions != null)
        {
            return new AIContext { Instructions = _cachedInstructions };
        }

        var instructions = new StringBuilder();

        foreach (var fileName in BootstrapFiles)
        {
            var filePath = GetFilePath(fileName);
            if (!File.Exists(filePath)) continue;

            try
            {
                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var sectionName = GetSectionName(fileName);
                    instructions.AppendLine($"## {sectionName}");
                    instructions.AppendLine(content);
                    instructions.AppendLine();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read bootstrap file: {FileName}", fileName);
            }
        }

        _cachedInstructions = instructions.Length > 0 ? instructions.ToString() : null;
        _cacheTime = DateTime.UtcNow;

        return new AIContext
        {
            Instructions = _cachedInstructions
        };
    }

    private string GetFilePath(string fileName)
    {
        return fileName switch
        {
            "AGENTS.md" => _workspace.GetAgentsFile(),
            "SOUL.md" => _workspace.GetSoulFile(),
            "USER.md" => _workspace.GetUserFile(),
            "TOOLS.md" => _workspace.GetToolsFile(),
            _ => Path.Combine(_workspace.GetWorkspacePath(), fileName)
        };
    }

    private static string GetSectionName(string fileName)
    {
        return fileName switch
        {
            "AGENTS.md" => "Agent Configuration",
            "SOUL.md" => "Personality",
            "USER.md" => "User Profile",
            "TOOLS.md" => "Tools Guide",
            _ => fileName.Replace(".md", "")
        };
    }
}
