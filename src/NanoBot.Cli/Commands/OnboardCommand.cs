using System.CommandLine;
using NanoBot.Core.Configuration;

namespace NanoBot.Cli.Commands;

public class OnboardCommand : ICliCommand
{
    public string Name => "onboard";
    public string Description => "Initialize nbot configuration and workspace";

    public Command CreateCommand()
    {
        var dirOption = new Option<string?>(
            name: "--dir",
            description: "Workspace directory path"
        );

        var nameOption = new Option<string>(
            name: "--name",
            description: "Agent name",
            getDefaultValue: () => "NanoBot"
        );

        var command = new Command(Name, Description)
        {
            dirOption,
            nameOption
        };

        command.SetHandler(async (context) =>
        {
            var dir = context.ParseResult.GetValueForOption(dirOption);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteOnboardAsync(dir, name, cancellationToken);
        });

        return command;
    }

    private async Task ExecuteOnboardAsync(string? dir, string name, CancellationToken cancellationToken)
    {
        var configPath = GetConfigPath();
        var workspacePath = dir ?? GetDefaultWorkspacePath();

        Console.WriteLine("🐈 nbot onboard\n");

        if (File.Exists(configPath))
        {
            Console.WriteLine($"Config already exists at {configPath}");
            Console.WriteLine("  [y] = overwrite with defaults (existing values will be lost)");
            Console.WriteLine("  [N] = refresh config, keeping existing values and adding new fields");
            Console.Write("Overwrite? [y/N]: ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (response == "y")
            {
                var config = new AgentConfig { Name = name };
                config.Workspace.Path = workspacePath;
                await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                Console.WriteLine($"✓ Config reset to defaults at {configPath}");
            }
            else
            {
                var config = await ConfigurationLoader.LoadAsync(configPath, cancellationToken);
                config.Name = name;
                await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                Console.WriteLine($"✓ Config refreshed at {configPath} (existing values preserved)");
            }
        }
        else
        {
            var config = new AgentConfig { Name = name };
            config.Workspace.Path = workspacePath;
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            Console.WriteLine($"✓ Created config at {configPath}");
        }

        var resolvedWorkspacePath = ResolvePath(workspacePath);

        if (!Directory.Exists(resolvedWorkspacePath))
        {
            Directory.CreateDirectory(resolvedWorkspacePath);
            Console.WriteLine($"✓ Created workspace at {resolvedWorkspacePath}");
        }

        await CreateWorkspaceTemplatesAsync(resolvedWorkspacePath, cancellationToken);

        Console.WriteLine("\n🐈 nbot is ready!");
        Console.WriteLine("\nNext steps:");
        Console.WriteLine("  1. Add your API key to ~/.nbot/config.json");
        Console.WriteLine("     Get one at: https://openrouter.ai/keys");
        Console.WriteLine("  2. Chat: nbot agent -m \"Hello!\"");
        Console.WriteLine("\nWant Telegram/WhatsApp? See: https://github.com/HKUDS/nanobot#-chat-apps");
    }

    private static string GetConfigPath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".nbot", "config.json");
    }

    private static string GetDefaultWorkspacePath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".nbot", "workspace");
    }

    private static string ResolvePath(string path)
    {
        if (path.StartsWith("~/"))
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDir, path[2..]);
        }
        return Path.GetFullPath(path);
    }

    private static async Task CreateWorkspaceTemplatesAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var templates = new Dictionary<string, string>
        {
            ["AGENTS.md"] = GetDefaultAgentsContent(),
            ["SOUL.md"] = GetDefaultSoulContent(),
            ["USER.md"] = GetDefaultUserContent()
        };

        foreach (var (filename, content) in templates)
        {
            var filePath = Path.Combine(workspacePath, filename);
            if (!File.Exists(filePath))
            {
                await File.WriteAllTextAsync(filePath, content, cancellationToken);
                Console.WriteLine($"  Created {filename}");
            }
        }

        var memoryDir = Path.Combine(workspacePath, "memory");
        if (!Directory.Exists(memoryDir))
        {
            Directory.CreateDirectory(memoryDir);
        }

        var memoryFile = Path.Combine(memoryDir, "MEMORY.md");
        if (!File.Exists(memoryFile))
        {
            await File.WriteAllTextAsync(memoryFile, GetDefaultMemoryContent(), cancellationToken);
            Console.WriteLine("  Created memory/MEMORY.md");
        }

        var historyFile = Path.Combine(memoryDir, "HISTORY.md");
        if (!File.Exists(historyFile))
        {
            await File.WriteAllTextAsync(historyFile, string.Empty, cancellationToken);
            Console.WriteLine("  Created memory/HISTORY.md");
        }

        var skillsDir = Path.Combine(workspacePath, "skills");
        if (!Directory.Exists(skillsDir))
        {
            Directory.CreateDirectory(skillsDir);
        }
    }

    private static string GetDefaultAgentsContent() => @"# Agent Instructions

You are a helpful AI assistant. Be concise, accurate, and friendly.

## Guidelines

- Always explain what you're doing before taking actions
- Ask for clarification when the request is ambiguous
- Use tools to help accomplish tasks
- Remember important information in memory/MEMORY.md; past events are logged in memory/HISTORY.md
";

    private static string GetDefaultSoulContent() => @"# Soul

I am nbot, a lightweight AI assistant.

## Personality

- Helpful and friendly
- Concise and to the point
- Curious and eager to learn

## Values

- Accuracy over speed
- User privacy and safety
- Transparency in actions
";

    private static string GetDefaultUserContent() => @"# User

Information about the user goes here.

## Preferences

- Communication style: (casual/formal)
- Timezone: (your timezone)
- Language: (your preferred language)
";

    private static string GetDefaultMemoryContent() => @"# Long-term Memory

This file stores important information that should persist across sessions.

## User Information

(Important facts about the user)

## Preferences

(User preferences learned over time)

## Important Notes

(Things to remember)
";
}
