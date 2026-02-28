using System.CommandLine;
using NanoBot.Core.Configuration;
using NanoBot.Cli.Services;

namespace NanoBot.Cli.Commands;

public class OnboardCommand : ICliCommand
{
    public string Name => "onboard";
    public string Description => "Initialize nbot configuration and workspace (and optionally configure LLM)";

    public Command CreateCommand()
    {
        var dirOption = new Option<string?>(
            name: "--dir",
            description: "Workspace directory path (default: .nbot in current directory)"
        );

        var nameOption = new Option<string>(
            name: "--name",
            description: "Agent name",
            getDefaultValue: () => "NanoBot"
        );

        var providerOption = new Option<string?>(
            name: "--provider",
            description: "LLM provider (openai, anthropic, openrouter, deepseek, moonshot, zhipu, ollama)"
        );
        providerOption.AddAlias("-p");

        var modelOption = new Option<string?>(
            name: "--model",
            description: "LLM model name"
        );
        modelOption.AddAlias("-m");

        var apiKeyOption = new Option<string?>(
            name: "--api-key",
            description: "API key for the LLM provider"
        );
        apiKeyOption.AddAlias("-k");

        var apiBaseOption = new Option<string?>(
            name: "--api-base",
            description: "API base URL (e.g. for third-party or proxy)"
        );

        var workspaceOption = new Option<string?>(
            name: "--workspace",
            description: "Workspace directory path"
        );
        workspaceOption.AddAlias("-w");

        var nonInteractiveOption = new Option<bool>(
            name: "--non-interactive",
            description: "Run without prompts; use defaults or provided options"
        );

        var command = new Command(Name, Description)
        {
            dirOption,
            nameOption,
            providerOption,
            modelOption,
            apiKeyOption,
            apiBaseOption,
            workspaceOption,
            nonInteractiveOption
        };

        command.SetHandler(async (context) =>
        {
            var dir = context.ParseResult.GetValueForOption(dirOption);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var provider = context.ParseResult.GetValueForOption(providerOption);
            var model = context.ParseResult.GetValueForOption(modelOption);
            var apiKey = context.ParseResult.GetValueForOption(apiKeyOption);
            var apiBase = context.ParseResult.GetValueForOption(apiBaseOption);
            var workspace = context.ParseResult.GetValueForOption(workspaceOption);
            var nonInteractive = context.ParseResult.GetValueForOption(nonInteractiveOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteOnboardAsync(dir, name, provider, model, apiKey, apiBase, workspace, nonInteractive, cancellationToken);
        });

        return command;
    }

    private async Task ExecuteOnboardAsync(
        string? dir,
        string name,
        string? provider,
        string? model,
        string? apiKey,
        string? apiBase,
        string? workspace,
        bool nonInteractive,
        CancellationToken cancellationToken)
    {
        var configPath = GetConfigPath();
        var workspacePath = dir ?? workspace ?? GetDefaultWorkspacePath();

        Console.WriteLine("🐈 nbot onboard\n");

        AgentConfig config;
        if (File.Exists(configPath))
        {
            Console.WriteLine($"Config already exists at {configPath}");
            Console.WriteLine("  [y] = overwrite with defaults (existing values will be lost)");
            Console.WriteLine("  [N] = refresh config, keeping existing values and adding new fields");
            Console.Write("Overwrite? [y/N]: ");
            var response = nonInteractive ? "n" : (Console.ReadLine()?.Trim().ToLowerInvariant());

            if (response == "y")
            {
                config = new AgentConfig { Name = name ?? "NanoBot" };
                config.Workspace.Path = workspacePath;
                await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                Console.WriteLine($"✓ Config reset to defaults at {configPath}");
            }
            else
            {
                config = await ConfigurationLoader.LoadAsync(configPath, cancellationToken);
                config.Name = name ?? "NanoBot";
                if (!string.IsNullOrEmpty(dir))
                {
                    config.Workspace.Path = dir;
                }
                else if (!string.IsNullOrEmpty(workspace))
                {
                    config.Workspace.Path = workspace;
                }
                await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                Console.WriteLine($"✓ Config refreshed at {configPath} (existing values preserved)");
            }
        }
        else
        {
            config = new AgentConfig { Name = name ?? "NanoBot" };
            config.Workspace.Path = workspacePath;
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            Console.WriteLine($"✓ Created config at {configPath}");
        }

        var resolvedWorkspacePath = ResolvePath(config.Workspace.Path);
        if (!Directory.Exists(resolvedWorkspacePath))
        {
            Directory.CreateDirectory(resolvedWorkspacePath);
            Console.WriteLine($"✓ Created workspace at {resolvedWorkspacePath}");
        }

        await CreateWorkspaceTemplatesAsync(resolvedWorkspacePath, cancellationToken);

        if (nonInteractive)
        {
            ApplyNonInteractiveOptions(config, provider, model, apiKey, apiBase, workspace);
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            PrintNextSteps(config, configPath);
            return;
        }

        Console.Write("\nConfigure LLM and workspace now? [Y/n]: ");
        var configureResponse = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (configureResponse == "n" || configureResponse == "no")
        {
            Console.WriteLine("\n🐈 nbot is ready!");
            PrintNextSteps(config, configPath);
            return;
        }

        await RunInteractiveAsync(config, configPath, cancellationToken);
    }

    private static void ApplyNonInteractiveOptions(
        AgentConfig config,
        string? provider,
        string? model,
        string? apiKey,
        string? apiBase,
        string? workspace)
    {
        var profileName = config.Llm.DefaultProfile ?? "default";
        if (!config.Llm.Profiles.ContainsKey(profileName))
        {
            config.Llm.Profiles[profileName] = new LlmProfile { Name = profileName };
        }
        var profile = config.Llm.Profiles[profileName];
        
        if (!string.IsNullOrEmpty(provider))
        {
            profile.Provider = provider.ToLowerInvariant();
        }

        if (!string.IsNullOrEmpty(model))
        {
            profile.Model = model;
        }
        else if (!string.IsNullOrEmpty(profile.Provider) &&
                 ConfigurationChecker.ProviderDefaultModels.TryGetValue(profile.Provider, out var defaultModel))
        {
            profile.Model = defaultModel;
        }

        if (!string.IsNullOrEmpty(apiKey))
        {
            profile.ApiKey = apiKey;
        }

        if (!string.IsNullOrEmpty(apiBase))
        {
            profile.ApiBase = apiBase;
        }
        else if (!string.IsNullOrEmpty(profile.Provider) &&
                 ConfigurationChecker.ProviderApiBases.TryGetValue(profile.Provider, out var defaultApiBase))
        {
            profile.ApiBase = defaultApiBase;
        }

        if (!string.IsNullOrEmpty(workspace))
        {
            config.Workspace.Path = workspace;
        }
    }

    private async Task RunInteractiveAsync(
        AgentConfig config,
        string configPath,
        CancellationToken cancellationToken)
    {
        var sections = new List<string> { "LLM Profiles", "Workspace", "Done" };
        var currentIndex = 0;

        while (currentIndex < sections.Count - 1)
        {
            Console.WriteLine("\nSelect section to configure:");
            for (var i = 0; i < sections.Count; i++)
            {
                var marker = i == currentIndex ? ">" : " ";
                Console.WriteLine($"  {marker} [{i + 1}] {sections[i]}");
            }
            Console.WriteLine();

            var key = Console.ReadKey(true);
            var keyChar = char.ToLowerInvariant(key.KeyChar);

            if (key.Key == ConsoleKey.UpArrow && currentIndex > 0)
            {
                currentIndex--;
            }
            else if (key.Key == ConsoleKey.DownArrow && currentIndex < sections.Count - 1)
            {
                currentIndex++;
            }
            else if (key.Key == ConsoleKey.Enter || keyChar == '\r')
            {
                var selectedSection = sections[currentIndex];
                Console.WriteLine($"Selected: {selectedSection}\n");

                switch (selectedSection)
                {
                    case "LLM Profiles":
                        await ConfigureLlmProfilesAsync(config, cancellationToken);
                        break;
                    case "Workspace":
                        ConfigureWorkspaceSection(config);
                        break;
                    case "Done":
                        await SaveAndFinishAsync(config, configPath, cancellationToken);
                        return;
                }

                Console.WriteLine();
            }
            else if (char.IsDigit(keyChar))
            {
                var index = keyChar - '1';
                if (index >= 0 && index < sections.Count)
                {
                    currentIndex = index;
                }
            }
        }

        await SaveAndFinishAsync(config, configPath, cancellationToken);
    }

    private async Task ConfigureLlmProfilesAsync(AgentConfig config, CancellationToken cancellationToken)
    {
        Console.WriteLine("=== LLM Profile Configuration ===\n");
        Console.WriteLine("You can configure multiple LLM profiles for different use cases.\n");

        var service = new LlmProfileConfigService();

        var hasDefaultProfile = config.Llm.Profiles.Count > 0;
        if (!hasDefaultProfile)
        {
            Console.WriteLine("No profiles configured yet. Let's create the default profile.\n");
            var profileName = config.Llm.DefaultProfile ?? "default";
            await service.ConfigureProfileInteractiveAsync(config, profileName, cancellationToken);
        }

        Console.Write("\nConfigure additional profiles? [y/N]: ");
        var response = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (response == "y" || response == "yes")
        {
            while (true)
            {
                Console.Write("\nEnter profile name (or press Enter to finish): ");
                var profileName = Console.ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(profileName))
                {
                    break;
                }

                if (config.Llm.Profiles.ContainsKey(profileName))
                {
                    Console.WriteLine($"Profile '{profileName}' already exists. Editing...");
                }

                await service.ConfigureProfileInteractiveAsync(config, profileName, cancellationToken);

                Console.Write("\nAdd another profile? [y/N]: ");
                var addMore = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (addMore != "y" && addMore != "yes")
                {
                    break;
                }
            }
        }

        if (config.Llm.Profiles.Count > 1)
        {
            Console.WriteLine("\nAvailable profiles:");
            foreach (var profileName in config.Llm.Profiles.Keys)
            {
                var profile = config.Llm.Profiles[profileName];
                var isDefault = profileName == (config.Llm.DefaultProfile ?? "default");
                var marker = isDefault ? "*" : " ";
                Console.WriteLine($"  {marker} {profileName} ({profile.Provider}/{profile.Model})");
            }

            Console.Write($"\nSet default profile [{config.Llm.DefaultProfile ?? "default"}]: ");
            var defaultInput = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(defaultInput) && config.Llm.Profiles.ContainsKey(defaultInput))
            {
                config.Llm.DefaultProfile = defaultInput;
                Console.WriteLine($"✓ Default profile set to '{defaultInput}'");
            }
        }
    }


    private void ConfigureWorkspaceSection(AgentConfig config)
    {
        Console.WriteLine("=== Workspace Configuration ===\n");

        var currentPath = config.Workspace.Path;
        Console.WriteLine($"Current workspace: {currentPath}");

        Console.Write("Workspace path [press Enter to keep current]: ");
        var input = Console.ReadLine()?.Trim();

        if (!string.IsNullOrWhiteSpace(input))
        {
            config.Workspace.Path = input;
        }

        var resolvedPath = ResolvePath(config.Workspace.Path);
        Console.WriteLine($"\n✓ Workspace: {resolvedPath}");
    }

    private async Task SaveAndFinishAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        var configDir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);

        Console.WriteLine($"\n✓ Configuration saved to {configPath}");

        var workspacePath = ResolvePath(config.Workspace.Path);
        if (!Directory.Exists(workspacePath))
        {
            Directory.CreateDirectory(workspacePath);
            Console.WriteLine($"✓ Created workspace at {workspacePath}");
        }

        await CreateWorkspaceTemplatesAsync(workspacePath, cancellationToken);

        Console.WriteLine("\n🐈 nbot is ready!");
        PrintNextSteps(config, configPath);
    }

    private static void PrintNextSteps(AgentConfig config, string configPath)
    {
        Console.WriteLine("\nNext steps:");
        Console.WriteLine("  • Chat: nbot agent");
        Console.WriteLine("  • Single message: nbot agent -m \"Hello!\"");
        Console.WriteLine("  • View config: nbot config --list");

        var profileName = config.Llm.DefaultProfile ?? "default";
        var profile = config.Llm.Profiles.GetValueOrDefault(profileName);
        
        if (profile != null && string.IsNullOrEmpty(profile.ApiKey) && profile.Provider != "ollama")
        {
            var envKey = ConfigurationChecker.ProviderEnvKeys.TryGetValue(profile.Provider ?? "", out var key)
                ? key
                : "API_KEY";
            Console.WriteLine($"\nNote: Set {envKey} environment variable or add apiKey to config file.");
        }
    }

    private static string GetConfigPath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".nbot", "config.json");
    }

    private static string GetDefaultWorkspacePath() => ".nbot";

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
