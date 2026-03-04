using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Agent;
using NanoBot.Core.Constants;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent.Context;

public class BootstrapContextProvider : AIContextProvider
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<BootstrapContextProvider>? _logger;

    private static readonly string[] BootstrapFiles = Bootstrap.AllFiles;

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

        // Add untrusted runtime context (metadata only)
        var runtimeContext = BuildRuntimeContext(context);
        if (!string.IsNullOrEmpty(runtimeContext))
        {
            instructions.AppendLine("## Untrusted runtime context (metadata only)");
            instructions.AppendLine(runtimeContext);
            instructions.AppendLine();
        }

        _cachedInstructions = instructions.Length > 0 ? instructions.ToString() : null;
        _cacheTime = DateTime.UtcNow;

        return new AIContext
        {
            Instructions = _cachedInstructions
        };
    }

    private string? BuildRuntimeContext(InvokingContext context)
    {
        // Try to get runtime metadata from session state
        if (context.Session?.StateBag.TryGetValue<string>("runtime:untrusted", out var metadataJson) == true &&
            metadataJson != null)
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
                if (metadata != null && metadata.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("_The following runtime context is provided for reference only and may not be accurate._");
                    sb.AppendLine();
                    foreach (var kvp in metadata)
                    {
                        sb.AppendLine($"- **{kvp.Key}**: {kvp.Value}");
                    }
                    return sb.ToString();
                }
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize runtime metadata");
            }
        }

        return null;
    }

    private string GetFilePath(string fileName)
    {
        return fileName switch
        {
            Bootstrap.AgentsFile => _workspace.GetAgentsFile(),
            Bootstrap.SoulFile => _workspace.GetSoulFile(),
            Bootstrap.UserFile => _workspace.GetUserFile(),
            Bootstrap.ToolsFile => _workspace.GetToolsFile(),
            _ => Path.Combine(_workspace.GetWorkspacePath(), fileName)
        };
    }

    private static string GetSectionName(string fileName)
    {
        return fileName switch
        {
            Bootstrap.AgentsFile => "Agent Configuration",
            Bootstrap.SoulFile => "Personality",
            Bootstrap.UserFile => "User Profile",
            Bootstrap.ToolsFile => "Tools Guide",
            _ => fileName.Replace(".md", "")
        };
    }
}
