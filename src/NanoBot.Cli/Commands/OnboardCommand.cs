using System.CommandLine;
using NanoBot.Core.Configuration;

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
        var sections = new List<string> { "LLM", "Workspace", "Done" };
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

        var profileName = config.Llm.DefaultProfile ?? "default";
        if (!config.Llm.Profiles.ContainsKey(profileName))
        {
            config.Llm.Profiles[profileName] = new LlmProfile { Name = profileName };
        }
        var profile = config.Llm.Profiles[profileName];
        
        var provider = await PromptProviderAsync(profile.Provider);
        if (provider == null)
        {
            Console.WriteLine("Cancelled.");
            return;
        }

        profile.Provider = provider;

        var defaultModel = ConfigurationChecker.ProviderDefaultModels.TryGetValue(provider, out var dm)
            ? dm
            : "gpt-4o-mini";

        Console.WriteLine($"\nDefault model for {provider}: {defaultModel}");
        Console.Write($"Model [{defaultModel}]: ");
        var modelInput = Console.ReadLine()?.Trim();
        profile.Model = string.IsNullOrWhiteSpace(modelInput) ? defaultModel : modelInput;

        if (provider != "ollama")
        {
            var apiKey = await PromptApiKeyAsync(provider);
            if (apiKey != null)
            {
                profile.ApiKey = apiKey;
            }
        }

        var defaultApiBase = profile.ApiBase;
        if (string.IsNullOrEmpty(defaultApiBase) &&
            ConfigurationChecker.ProviderApiBases.TryGetValue(provider, out var ab))
        {
            defaultApiBase = ab;
        }
        if (string.IsNullOrEmpty(defaultApiBase))
        {
            defaultApiBase = "https://api.openai.com/v1";
        }

        Console.WriteLine("\nAPI URL (optional, for third-party or proxy). Press Enter for default.");
        Console.Write($"API URL [{defaultApiBase}]: ");
        var urlInput = Console.ReadLine()?.Trim();
        profile.ApiBase = string.IsNullOrWhiteSpace(urlInput) ? defaultApiBase : urlInput;

        Console.WriteLine($"\n✓ LLM configured:");
        Console.WriteLine($"  Provider: {profile.Provider}");
        Console.WriteLine($"  Model: {profile.Model}");
        Console.WriteLine($"  API Key: {(string.IsNullOrEmpty(profile.ApiKey) ? "(using environment variable)" : MaskApiKey(profile.ApiKey))}");
        Console.WriteLine($"  API URL: {MaskApiUrl(profile.ApiBase)}");
    }

    private async Task<string?> PromptProviderAsync(string? currentProvider)
    {
        var providers = ConfigurationChecker.SupportedProviders.ToList();
        var currentIndex = 0;

        if (!string.IsNullOrEmpty(currentProvider))
        {
            var existingIndex = providers.FindIndex(p => p.Equals(currentProvider, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                currentIndex = existingIndex;
            }
        }

        Console.WriteLine("Select LLM provider:");

        while (true)
        {
            for (var i = 0; i < providers.Count; i++)
            {
                var marker = i == currentIndex ? ">" : " ";
                var hint = ConfigurationChecker.ProviderDefaultModels.TryGetValue(providers[i], out var model)
                    ? $" (default: {model})"
                    : "";
                Console.WriteLine($"  {marker} [{i + 1}] {providers[i]}{hint}");
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

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 8)
        {
            return "***";
        }
        return $"{apiKey[..4]}...{apiKey[^4..]}";
    }

    private static string MaskApiUrl(string? apiUrl)
    {
        if (string.IsNullOrEmpty(apiUrl))
        {
            return "(default)";
        }
        try
        {
            var uri = new Uri(apiUrl);
            return uri.Host + (string.IsNullOrEmpty(uri.PathAndQuery) || uri.PathAndQuery == "/" ? "" : uri.PathAndQuery);
        }
        catch
        {
            return apiUrl.Length > 40 ? apiUrl[..40] + "..." : apiUrl;
        }
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
