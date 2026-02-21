using System.CommandLine;
using System.Text.Json;
using NanoBot.Core.Configuration;

namespace NanoBot.Cli.Commands;

public class ConfigureCommand : ICliCommand
{
    public string Name => "configure";
    public string Description => "Interactive configuration wizard for LLM and settings";

    public Command CreateCommand()
    {
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

        var workspaceOption = new Option<string?>(
            name: "--workspace",
            description: "Workspace directory path"
        );
        workspaceOption.AddAlias("-w");

        var nonInteractiveOption = new Option<bool>(
            name: "--non-interactive",
            description: "Run without prompts, use defaults or provided options"
        );

        var command = new Command(Name, Description)
        {
            providerOption,
            modelOption,
            apiKeyOption,
            workspaceOption,
            nonInteractiveOption
        };

        command.SetHandler(async (context) =>
        {
            var provider = context.ParseResult.GetValueForOption(providerOption);
            var model = context.ParseResult.GetValueForOption(modelOption);
            var apiKey = context.ParseResult.GetValueForOption(apiKeyOption);
            var workspace = context.ParseResult.GetValueForOption(workspaceOption);
            var nonInteractive = context.ParseResult.GetValueForOption(nonInteractiveOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteConfigureAsync(provider, model, apiKey, workspace, nonInteractive, cancellationToken);
        });

        return command;
    }

    private async Task ExecuteConfigureAsync(
        string? provider,
        string? model,
        string? apiKey,
        string? workspace,
        bool nonInteractive,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("🐈 nbot configure\n");

        var configPath = ConfigurationChecker.GetDefaultConfigPath();
        AgentConfig config;

        if (File.Exists(configPath))
        {
            try
            {
                config = await ConfigurationLoader.LoadAsync(configPath, cancellationToken);
                Console.WriteLine($"Found existing config at {configPath}\n");
            }
            catch
            {
                config = new AgentConfig();
                Console.WriteLine($"Creating new config at {configPath}\n");
            }
        }
        else
        {
            config = new AgentConfig();
            Console.WriteLine($"Creating new config at {configPath}\n");
        }

        if (nonInteractive)
        {
            await RunNonInteractiveAsync(config, provider, model, apiKey, workspace, configPath, cancellationToken);
            return;
        }

        await RunInteractiveAsync(config, configPath, cancellationToken);
    }

    private async Task RunInteractiveAsync(
        AgentConfig config,
        string configPath,
        CancellationToken cancellationToken)
    {
        var sections = new List<string> { "LLM", "Workspace", "Done" };
        var currentIndex = 0;

        while (currentIndex < sections.Count - 1)
        {
            Console.WriteLine("Select section to configure:");
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
                    case "LLM":
                        await ConfigureLlmSectionAsync(config, cancellationToken);
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

    private async Task ConfigureLlmSectionAsync(AgentConfig config, CancellationToken cancellationToken)
    {
        Console.WriteLine("=== LLM Configuration ===\n");

        var provider = await PromptProviderAsync(config.Llm.Provider);
        if (provider == null)
        {
            Console.WriteLine("Cancelled.");
            return;
        }

        config.Llm.Provider = provider;

        var defaultModel = ConfigurationChecker.ProviderDefaultModels.TryGetValue(provider, out var dm)
            ? dm
            : "gpt-4o-mini";

        Console.WriteLine($"\nDefault model for {provider}: {defaultModel}");
        Console.Write($"Model [{defaultModel}]: ");
        var modelInput = Console.ReadLine()?.Trim();
        config.Llm.Model = string.IsNullOrWhiteSpace(modelInput) ? defaultModel : modelInput;

        if (provider != "ollama")
        {
            var apiKey = await PromptApiKeyAsync(provider);
            if (apiKey != null)
            {
                config.Llm.ApiKey = apiKey;
            }
        }

        if (ConfigurationChecker.ProviderApiBases.TryGetValue(provider, out var apiBase))
        {
            config.Llm.ApiBase = apiBase;
        }

        Console.WriteLine($"\n✓ LLM configured:");
        Console.WriteLine($"  Provider: {config.Llm.Provider}");
        Console.WriteLine($"  Model: {config.Llm.Model}");
        Console.WriteLine($"  API Key: {(string.IsNullOrEmpty(config.Llm.ApiKey) ? "(using environment variable)" : MaskApiKey(config.Llm.ApiKey))}");
    }

    private async Task<string?> PromptProviderAsync(string? currentProvider)
    {
        var providers = ConfigurationChecker.SupportedProviders.ToList();
        var currentIndex = 0;

        if (!string.IsNullOrEmpty(currentProvider))
        {
            var existingIndex = providers.ToList().FindIndex(p => p.Equals(currentProvider, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                currentIndex = existingIndex;
            }
        }

        Console.WriteLine("Select LLM provider:");

        while (true)
        {
            Console.SetCursorPosition(0, Console.CursorTop - providers.Count - 1);

            for (var i = 0; i < providers.Count; i++)
            {
                var marker = i == currentIndex ? ">" : " ";
                var hint = ConfigurationChecker.ProviderDefaultModels.TryGetValue(providers[i], out var model)
                    ? $" (default: {model})"
                    : "";
                Console.WriteLine($"  {marker} [{i + 1}] {providers[i]}{hint}    ");
            }

            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.UpArrow && currentIndex > 0)
            {
                currentIndex--;
            }
            else if (key.Key == ConsoleKey.DownArrow && currentIndex < providers.Count - 1)
            {
                currentIndex++;
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine($"\nSelected: {providers[currentIndex]}\n");
                return providers[currentIndex];
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                return null;
            }
            else if (char.IsDigit(key.KeyChar))
            {
                var index = key.KeyChar - '1';
                if (index >= 0 && index < providers.Count)
                {
                    Console.WriteLine($"\nSelected: {providers[index]}\n");
                    return providers[index];
                }
            }
        }
    }

    private async Task<string?> PromptApiKeyAsync(string provider)
    {
        var envKey = ConfigurationChecker.ProviderEnvKeys.TryGetValue(provider, out var key) ? key : null;
        var existingEnvValue = envKey != null ? Environment.GetEnvironmentVariable(envKey) : null;

        if (!string.IsNullOrEmpty(existingEnvValue))
        {
            Console.WriteLine($"\nFound {envKey} in environment.");
            Console.Write("Use environment variable? [Y/n]: ");
            var useEnv = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (useEnv != "n" && useEnv != "no")
            {
                return null;
            }
        }

        if (ConfigurationChecker.ProviderKeyUrls.TryGetValue(provider, out var keyUrl))
        {
            Console.WriteLine($"\nGet your API key at: {keyUrl}");
        }

        Console.Write("\nAPI Key: ");
        var apiKey = ReadLineMasked();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("No API key entered. You can set it later via environment variable or config file.");
            return null;
        }

        return apiKey;
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

    private async Task RunNonInteractiveAsync(
        AgentConfig config,
        string? provider,
        string? model,
        string? apiKey,
        string? workspace,
        string configPath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(provider))
        {
            config.Llm.Provider = provider.ToLowerInvariant();
        }

        if (!string.IsNullOrEmpty(model))
        {
            config.Llm.Model = model;
        }
        else if (!string.IsNullOrEmpty(config.Llm.Provider))
        {
            if (ConfigurationChecker.ProviderDefaultModels.TryGetValue(config.Llm.Provider, out var defaultModel))
            {
                config.Llm.Model = defaultModel;
            }
        }

        if (!string.IsNullOrEmpty(apiKey))
        {
            config.Llm.ApiKey = apiKey;
        }

        if (!string.IsNullOrEmpty(workspace))
        {
            config.Workspace.Path = workspace;
        }

        if (ConfigurationChecker.ProviderApiBases.TryGetValue(config.Llm.Provider ?? "", out var apiBase))
        {
            config.Llm.ApiBase = apiBase;
        }

        await SaveAndFinishAsync(config, configPath, cancellationToken);
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
        Console.WriteLine("\nNext steps:");
        Console.WriteLine("  • Chat: nbot agent");
        Console.WriteLine("  • Single message: nbot agent -m \"Hello!\"");
        Console.WriteLine("  • View config: nbot config --list");

        if (string.IsNullOrEmpty(config.Llm.ApiKey) && config.Llm.Provider != "ollama")
        {
            var envKey = ConfigurationChecker.ProviderEnvKeys.TryGetValue(config.Llm.Provider ?? "", out var key)
                ? key
                : "API_KEY";
            Console.WriteLine($"\nNote: Set {envKey} environment variable or add apiKey to config file.");
        }
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

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 8)
        {
            return "***";
        }
        return $"{apiKey[..4]}...{apiKey[^4..]}";
    }

    private static string ReadLineMasked()
    {
        var result = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace && result.Length > 0)
            {
                result.Remove(result.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                result.Append(key.KeyChar);
                Console.Write("*");
            }
        }
        return result.ToString();
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
