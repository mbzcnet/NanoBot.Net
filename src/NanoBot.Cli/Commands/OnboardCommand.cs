using System.CommandLine;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;
using NanoBot.Core.Workspace;
using NanoBot.Cli.Services;
using NanoBot.Infrastructure.Browser;
using NanoBot.Infrastructure.Resources;
using NanoBot.Infrastructure.Workspace;

namespace NanoBot.Cli.Commands;

public class OnboardCommand : ICliCommand
{
    public string Name => "onboard";
    public string Description => "Initialize nbot configuration and workspace with interactive setup";

    public Command CreateCommand()
    {
        var dirOption = new Option<string?>(
            name: "--dir",
            description: "Workspace directory path (default: ~/.nbot/workspace)"
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

        var skipBrowserInstallOption = new Option<bool>(
            name: "--skip-browser-install",
            description: "Skip Playwright browser installation"
        );

        var skipOmniParserOption = new Option<bool>(
            name: "--skip-omniparser",
            description: "Skip OmniParser (RPA vision) installation"
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
            nonInteractiveOption,
            skipBrowserInstallOption,
            skipOmniParserOption
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
            var skipBrowserInstall = context.ParseResult.GetValueForOption(skipBrowserInstallOption);
            var skipOmniParser = context.ParseResult.GetValueForOption(skipOmniParserOption);
            var cancellationToken = context.GetCancellationToken();
            await ExecuteOnboardAsync(dir, name ?? "NanoBot", provider, model, apiKey, apiBase, workspace, nonInteractive, skipBrowserInstall, skipOmniParser, cancellationToken);
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
        bool skipBrowserInstall,
        bool skipOmniParser,
        CancellationToken cancellationToken)
    {
        var configPath = GetConfigPath();
        var workspacePath = dir ?? workspace ?? GetDefaultWorkspacePath();

        Console.WriteLine("🐈 nbot onboard\n");

        // Step 1: Environment check
        var envInfo = CheckEnvironment();
        PrintEnvironmentInfo(envInfo);

        // Step 2: Handle config file
        AgentConfig config = await InitializeConfigAsync(configPath, name, workspacePath, envInfo, nonInteractive, cancellationToken);

        // Ensure workspace exists
        var resolvedWorkspacePath = ResolvePath(config.Workspace.Path);
        if (!Directory.Exists(resolvedWorkspacePath))
        {
            Directory.CreateDirectory(resolvedWorkspacePath);
            Console.WriteLine($"✓ Created workspace at {resolvedWorkspacePath}");
        }

        await InitializeWorkspaceAsync(configPath, resolvedWorkspacePath, cancellationToken);

        // Step 3: Dependency installation phase (Playwright/OmniParser)
        if (nonInteractive)
        {
            if (!skipBrowserInstall)
            {
                await InstallPlaywrightBrowsersAsync(config, configPath, true, cancellationToken);
            }
            if (!skipOmniParser)
            {
                await InstallOmniParserAsync(config, configPath, true, cancellationToken);
            }
            ApplyNonInteractiveOptions(config, provider, model, apiKey, apiBase, workspace);
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            PrintNextSteps(config, configPath);
            return;
        }

        // Step 4: Interactive dependency install prompts
        var browserInstalled = await IsPlaywrightInstalledAsync(cancellationToken);
        var omniInstalled = await IsOmniParserInstalledAsync(configPath);

        if (!browserInstalled)
        {
            Console.WriteLine("\n=== Browser Tools (Playwright) ===");
            Console.WriteLine("Playwright is not installed. Install now for browser automation? [Y/n]: ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "n" && response != "no")
            {
                await InstallPlaywrightBrowsersAsync(config, configPath, false, cancellationToken);
            }
        }

        if (!omniInstalled)
        {
            Console.WriteLine("\n=== RPA Vision (OmniParser) ===");
            Console.WriteLine("OmniParser is not installed. Install now for RPA vision? [y/N]: ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response == "y" || response == "yes")
            {
                await InstallOmniParserAsync(config, configPath, false, cancellationToken);
            }
        }

        // Step 5: Interactive configuration menu
        await RunConfigurationMenuAsync(config, configPath, envInfo, cancellationToken);
    }

    /// <summary>
    /// Check environment information
    /// </summary>
    private EnvironmentInfo CheckEnvironment()
    {
        var info = new EnvironmentInfo
        {
            OsPlatform = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            HasGui = DetectGuiEnvironment(),
            ConfigExists = File.Exists(GetConfigPath())
        };

        return info;
    }

    /// <summary>
    /// Detect if GUI environment is available
    /// </summary>
    private bool DetectGuiEnvironment()
    {
        // Check for common GUI environment variables
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return true; // Windows usually has GUI
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Check for DISPLAY or WAYLAND_DISPLAY
            var display = Environment.GetEnvironmentVariable("DISPLAY");
            var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

            if (!string.IsNullOrEmpty(display) || !string.IsNullOrEmpty(wayland))
            {
                return true;
            }

            // Check if running in WSL with GUI support
            var wslDistro = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");
            if (!string.IsNullOrEmpty(wslDistro))
            {
                // WSL might have GUI support via WSLg
                return true;
            }

            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return true; // macOS usually has GUI
        }

        return false;
    }

    /// <summary>
    /// Print environment information
    /// </summary>
    private void PrintEnvironmentInfo(EnvironmentInfo info)
    {
        Console.WriteLine("=== Environment Check ===");
        Console.WriteLine($"  OS: {info.OsPlatform}");
        Console.WriteLine($"  GUI: {(info.HasGui ? "Available" : "Not detected (headless)")}");
        Console.WriteLine($"  Config: {(info.ConfigExists ? "Exists" : "Not found")}");
        Console.WriteLine();
    }

    /// <summary>
    /// Initialize configuration file
    /// </summary>
    private async Task<AgentConfig> InitializeConfigAsync(
        string configPath,
        string name,
        string workspacePath,
        EnvironmentInfo envInfo,
        bool nonInteractive,
        CancellationToken cancellationToken)
    {
        AgentConfig config;

        if (File.Exists(configPath))
        {
            Console.WriteLine($"Config already exists at {configPath}");
            config = await ConfigurationLoader.LoadAsync(configPath, cancellationToken);
            config.Name = name;
            ApplyEnvironmentSettings(config, envInfo);
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            Console.WriteLine($"✓ Config loaded from {configPath}");
        }
        else
        {
            config = CreateDefaultAgentConfig(name, workspacePath, envInfo);
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            Console.WriteLine($"✓ Created config at {configPath}");
        }

        return config;
    }

    /// <summary>
    /// Apply environment-specific settings to config
    /// </summary>
    private void ApplyEnvironmentSettings(AgentConfig config, EnvironmentInfo envInfo)
    {
        // Disable rap tool if no GUI
        if (!envInfo.HasGui)
        {
            // Note: rap tool would be disabled here if it existed in config
            // For now, we just note this in the browser config
            if (config.Browser != null)
            {
                // Browser can work in headless mode, so we keep it enabled
                // But we note that GUI is not available
            }
        }
    }

    /// <summary>
    /// Run the main configuration menu
    /// </summary>
    private async Task RunConfigurationMenuAsync(
        AgentConfig config,
        string configPath,
        EnvironmentInfo envInfo,
        CancellationToken cancellationToken)
    {
        var menuItems = new List<MenuItem>
        {
            new("LLM Configuration", async () => await ConfigureLlmAsync(config, configPath, cancellationToken)),
            new("Channels Configuration", async () => await ConfigureChannelsAsync(config, configPath, cancellationToken)),
            new("Tools Configuration", async () => await ConfigureToolsAsync(config, configPath, envInfo, cancellationToken)),
            new("Memory Configuration", async () => await ConfigureMemoryAsync(config, configPath, cancellationToken)),
            new("Security Configuration", async () => ConfigureSecurity(config)),
            new("MCP Configuration", async () => await ConfigureMcpAsync(config, configPath, cancellationToken)),
            new("Heartbeat Configuration", async () => await ConfigureHeartbeatAsync(config, configPath, cancellationToken)),
            new("WebUI Configuration", async () => await ConfigureWebUiAsync(config, configPath, cancellationToken)),
            new("Agent Settings", async () => await ConfigureAgentSettingsAsync(config, configPath, cancellationToken)),
            new("Workspace Configuration", async () => ConfigureWorkspace(config)),
            new("Start Agent Mode", null),
            new("Save & Exit", null)
        };

        var currentIndex = 0;

        while (true)
        {
            Console.WriteLine("\n=== Configuration Menu ===\n");
            Console.WriteLine("Select an option:\n");

            for (var i = 0; i < menuItems.Count; i++)
            {
                var marker = i == currentIndex ? ">" : " ";
                var number = i + 1;
                Console.WriteLine($"  {marker} [{number}] {menuItems[i].Name}");
            }

            Console.WriteLine($"\nNavigation: [↑/↓] or [1-{menuItems.Count}], [Enter] to select, [Q] to quit");

            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Q)
            {
                Console.WriteLine("\nSave changes before exiting? [Y/n]: ");
                var save = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (save != "n" && save != "no")
                {
                    await SaveConfigAsync(config, configPath, cancellationToken);
                }
                Console.WriteLine("\n🐈 nbot is ready!");
                PrintNextSteps(config, configPath);
                return;
            }

            if (key.Key == ConsoleKey.UpArrow && currentIndex > 0)
            {
                currentIndex--;
            }
            else if (key.Key == ConsoleKey.DownArrow && currentIndex < menuItems.Count - 1)
            {
                currentIndex++;
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                if (currentIndex == menuItems.Count - 1) // Save & Exit
                {
                    await SaveConfigAsync(config, configPath, cancellationToken);
                    Console.WriteLine("\n🐈 nbot is ready!");
                    PrintNextSteps(config, configPath);
                    return;
                }
                else if (currentIndex == menuItems.Count - 2) // Start Agent Mode
                {
                    await SaveConfigAsync(config, configPath, cancellationToken);
                    await StartAgentModeAsync(config, configPath, cancellationToken);
                    return;
                }

                Console.WriteLine($"\n--- {menuItems[currentIndex].Name} ---\n");
                await menuItems[currentIndex].Action!();
            }
            else if (char.IsDigit(key.KeyChar) || (key.KeyChar >= '\uFF10' && key.KeyChar <= '\uFF19'))
            {
                var normalizedChar = NormalizeInputChar(key.KeyChar);
                var index = normalizedChar - '1';
                if (index >= 0 && index < menuItems.Count)
                {
                    currentIndex = index;

                    if (currentIndex == menuItems.Count - 1) // Save & Exit
                    {
                        await SaveConfigAsync(config, configPath, cancellationToken);
                        Console.WriteLine("\n🐈 nbot is ready!");
                        PrintNextSteps(config, configPath);
                        return;
                    }
                    else if (currentIndex == menuItems.Count - 2) // Start Agent Mode
                    {
                        await SaveConfigAsync(config, configPath, cancellationToken);
                        await StartAgentModeAsync(config, configPath, cancellationToken);
                        return;
                    }

                    Console.WriteLine($"\n--- {menuItems[currentIndex].Name} ---\n");
                    await menuItems[currentIndex].Action!();
                }
            }
        }
    }

    /// <summary>
    /// Configure LLM profiles
    /// </summary>
    private async Task ConfigureLlmAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        var service = new LlmProfileConfigService();

        // Show current status
        if (config.Llm.Profiles.Count == 0)
        {
            Console.WriteLine("No LLM profiles configured.\n");
        }
        else
        {
            Console.WriteLine("Current LLM Profiles:");
            foreach (var profileName in config.Llm.Profiles.Keys)
            {
                var profile = config.Llm.Profiles[profileName];
                var isDefault = profileName == (config.Llm.DefaultProfile ?? "default");
                var marker = isDefault ? "*" : " ";
                Console.WriteLine($"  {marker} {profileName}: {profile.Provider}/{profile.Model}");
            }
            Console.WriteLine();
        }

        // Sub-menu for LLM configuration
        while (true)
        {
            Console.WriteLine("LLM Configuration Options:");
            Console.WriteLine("  [1] Add/Edit Profile");
            Console.WriteLine("  [2] Set Default Profile");
            Console.WriteLine("  [3] Delete Profile");
            Console.WriteLine("  [4] Back to Main Menu");
            Console.Write("\nSelect option: ");

            var key = Console.ReadKey(true);
            Console.WriteLine(key.KeyChar);

            var option = NormalizeInputChar(key.KeyChar);

            switch (option)
            {
                case '1':
                    await ConfigureLlmProfilesInteractiveAsync(config, service, cancellationToken);
                    break;
                case '2':
                    SetDefaultProfile(config);
                    break;
                case '3':
                    DeleteLlmProfile(config);
                    break;
                case '4':
                    return;
                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }

            // Save after each operation
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
        }
    }

    /// <summary>
    /// Interactive LLM profile configuration
    /// </summary>
    private async Task ConfigureLlmProfilesInteractiveAsync(
        AgentConfig config,
        LlmProfileConfigService service,
        CancellationToken cancellationToken)
    {
        var hasDefaultProfile = config.Llm.Profiles.Count > 0;
        if (!hasDefaultProfile)
        {
            Console.WriteLine("No profiles configured yet. Let's create the default profile.\n");
            var profileName = string.IsNullOrEmpty(config.Llm.DefaultProfile) ? "default" : config.Llm.DefaultProfile;
            await service.ConfigureProfileInteractiveAsync(config, profileName, cancellationToken);
            config.Llm.DefaultProfile = profileName;
        }
        else
        {
            Console.Write("\nEnter profile name (or press Enter for 'default'): ");
            var profileName = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(profileName))
            {
                profileName = "default";
            }

            if (config.Llm.Profiles.ContainsKey(profileName))
            {
                Console.WriteLine($"Profile '{profileName}' already exists. Editing...");
            }

            await service.ConfigureProfileInteractiveAsync(config, profileName, cancellationToken);
        }
    }

    /// <summary>
    /// Set default LLM profile
    /// </summary>
    private void SetDefaultProfile(AgentConfig config)
    {
        if (config.Llm.Profiles.Count == 0)
        {
            Console.WriteLine("\nNo profiles available.");
            return;
        }

        var profiles = config.Llm.Profiles.Keys.ToList();
        Console.WriteLine("\nSelect default profile:");
        for (var i = 0; i < profiles.Count; i++)
        {
            var isDefault = profiles[i] == (config.Llm.DefaultProfile ?? "default");
            var marker = isDefault ? "*" : " ";
            Console.WriteLine($"  {marker} [{i + 1}] {profiles[i]}");
        }
        Console.Write("\nEnter number or name: ");
        var input = Console.ReadLine()?.Trim();

        string? selectedProfile = null;
        if (int.TryParse(input, out var index) && index > 0 && index <= profiles.Count)
        {
            selectedProfile = profiles[index - 1];
        }
        else if (!string.IsNullOrWhiteSpace(input) && config.Llm.Profiles.ContainsKey(input))
        {
            selectedProfile = input;
        }

        if (selectedProfile == null)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        config.Llm.DefaultProfile = selectedProfile;
        Console.WriteLine($"✓ Default profile set to '{selectedProfile}'.");
    }

    /// <summary>
    /// Delete an LLM profile
    /// </summary>
    private void DeleteLlmProfile(AgentConfig config)
    {
        if (config.Llm.Profiles.Count == 0)
        {
            Console.WriteLine("\nNo profiles to delete.");
            return;
        }

        var profiles = config.Llm.Profiles.Keys.ToList();
        Console.WriteLine("\nSelect profile to delete:");
        for (var i = 0; i < profiles.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {profiles[i]}");
        }
        Console.Write("\nEnter number or name: ");
        var input = Console.ReadLine()?.Trim();

        string? selectedProfile = null;
        if (int.TryParse(input, out var index) && index > 0 && index <= profiles.Count)
        {
            selectedProfile = profiles[index - 1];
        }
        else if (!string.IsNullOrWhiteSpace(input) && config.Llm.Profiles.ContainsKey(input))
        {
            selectedProfile = input;
        }

        if (selectedProfile == null)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        if (selectedProfile == (config.Llm.DefaultProfile ?? "default"))
        {
            Console.WriteLine($"Cannot delete the default profile '{selectedProfile}'.");
            Console.WriteLine("Please set a different default profile first.");
            return;
        }

        Console.Write($"Are you sure you want to delete profile '{selectedProfile}'? [y/N]: ");
        var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (confirm == "y" || confirm == "yes")
        {
            config.Llm.Profiles.Remove(selectedProfile);
            Console.WriteLine($"✓ Profile '{selectedProfile}' deleted.");
        }
        else
        {
            Console.WriteLine("Cancelled.");
        }
    }

    /// <summary>
    /// Configure Channels
    /// </summary>
    private async Task ConfigureChannelsAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        while (true)
        {
            Console.WriteLine("\n=== Channels Configuration ===\n");
            Console.WriteLine("Available Channels:");
            Console.WriteLine($"  [1] Telegram    {(config.Channels.Telegram?.Enabled == true ? "[enabled]" : "[disabled]")}");
            Console.WriteLine($"  [2] Discord     {(config.Channels.Discord?.Enabled == true ? "[enabled]" : "[disabled]")}");
            Console.WriteLine($"  [3] Feishu      {(config.Channels.Feishu?.Enabled == true ? "[enabled]" : "[disabled]")}");
            Console.WriteLine($"  [4] DingTalk    {(config.Channels.DingTalk?.Enabled == true ? "[enabled]" : "[disabled]")}");
            Console.WriteLine($"  [5] Slack       {(config.Channels.Slack?.Enabled == true ? "[enabled]" : "[disabled]")}");
            Console.WriteLine($"  [6] Email       {(config.Channels.Email?.Enabled == true ? "[enabled]" : "[disabled]")}");
            Console.WriteLine($"  [7] Matrix      {(config.Channels.Matrix?.Enabled == true ? "[enabled]" : "[disabled]")}");
            Console.WriteLine($"  [8] WeiXin      {(config.Channels.WeiXin?.Enabled == true ? "[enabled]" : "[disabled]")}");
            Console.WriteLine("  [9] Back to Main Menu");

            Console.Write("\nSelect channel to configure: ");
            var key = Console.ReadKey(true);
            Console.WriteLine(key.KeyChar);

            var option = NormalizeInputChar(key.KeyChar);

            switch (option)
            {
                case '1':
                    ConfigureTelegram(config);
                    break;
                case '2':
                    ConfigureDiscord(config);
                    break;
                case '3':
                    ConfigureFeishu(config);
                    break;
                case '4':
                    ConfigureDingTalk(config);
                    break;
                case '5':
                    ConfigureSlack(config);
                    break;
                case '6':
                    ConfigureEmail(config);
                    break;
                case '7':
                    ConfigureMatrix(config);
                    break;
                case '8':
                    ConfigureWeiXin(config);
                    break;
                case '9':
                    await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                    return;
                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }

            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
        }
    }

    /// <summary>
    /// Configure Telegram channel
    /// </summary>
    private void ConfigureTelegram(AgentConfig config)
    {
        Console.WriteLine("\n=== Telegram Configuration ===\n");

        if (config.Channels.Telegram == null)
        {
            config.Channels.Telegram = new TelegramConfig();
        }

        var telegram = config.Channels.Telegram;

        Console.Write($"Enable Telegram? [{(telegram.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        telegram.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && telegram.Enabled);

        if (!telegram.Enabled)
        {
            Console.WriteLine("Telegram disabled.");
            return;
        }

        Console.Write($"Bot Token [{MaskString(telegram.Token)}]: ");
        var token = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(token))
        {
            telegram.Token = token;
        }

        Console.Write($"Allowed Users (comma-separated) [{string.Join(",", telegram.AllowFrom)}]: ");
        var users = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(users))
        {
            telegram.AllowFrom = users.Split(',').Select(u => u.Trim()).ToArray();
        }

        Console.WriteLine("✓ Telegram configuration saved.");
    }

    /// <summary>
    /// Configure Discord channel
    /// </summary>
    private void ConfigureDiscord(AgentConfig config)
    {
        Console.WriteLine("\n=== Discord Configuration ===\n");

        if (config.Channels.Discord == null)
        {
            config.Channels.Discord = new DiscordConfig();
        }

        var discord = config.Channels.Discord;

        Console.Write($"Enable Discord? [{(discord.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        discord.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && discord.Enabled);

        if (!discord.Enabled)
        {
            Console.WriteLine("Discord disabled.");
            return;
        }

        Console.Write($"Bot Token [{MaskString(discord.Token)}]: ");
        var token = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(token))
        {
            discord.Token = token;
        }

        Console.WriteLine("✓ Discord configuration saved.");
    }

    /// <summary>
    /// Configure Feishu channel
    /// </summary>
    private void ConfigureFeishu(AgentConfig config)
    {
        Console.WriteLine("\n=== Feishu Configuration ===\n");

        if (config.Channels.Feishu == null)
        {
            config.Channels.Feishu = new FeishuConfig();
        }

        var feishu = config.Channels.Feishu;

        Console.Write($"Enable Feishu? [{(feishu.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        feishu.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && feishu.Enabled);

        if (!feishu.Enabled)
        {
            Console.WriteLine("Feishu disabled.");
            return;
        }

        Console.Write($"App ID [{feishu.AppId}]: ");
        var appId = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(appId))
        {
            feishu.AppId = appId;
        }

        Console.Write($"App Secret [{MaskString(feishu.AppSecret)}]: ");
        var secret = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(secret))
        {
            feishu.AppSecret = secret;
        }

        Console.Write($"Encrypt Key [{MaskString(feishu.EncryptKey)}]: ");
        var encryptKey = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(encryptKey))
        {
            feishu.EncryptKey = encryptKey;
        }

        Console.Write($"Verification Token [{MaskString(feishu.VerificationToken)}]: ");
        var verificationToken = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(verificationToken))
        {
            feishu.VerificationToken = verificationToken;
        }

        Console.WriteLine("✓ Feishu configuration saved.");
    }

    /// <summary>
    /// Configure DingTalk channel
    /// </summary>
    private void ConfigureDingTalk(AgentConfig config)
    {
        Console.WriteLine("\n=== DingTalk Configuration ===\n");

        if (config.Channels.DingTalk == null)
        {
            config.Channels.DingTalk = new DingTalkConfig();
        }

        var dingTalk = config.Channels.DingTalk;

        Console.Write($"Enable DingTalk? [{(dingTalk.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        dingTalk.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && dingTalk.Enabled);

        if (!dingTalk.Enabled)
        {
            Console.WriteLine("DingTalk disabled.");
            return;
        }

        Console.Write($"Client ID [{dingTalk.ClientId}]: ");
        var clientId = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            dingTalk.ClientId = clientId;
        }

        Console.Write($"Client Secret [{MaskString(dingTalk.ClientSecret)}]: ");
        var secret = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(secret))
        {
            dingTalk.ClientSecret = secret;
        }

        Console.WriteLine("✓ DingTalk configuration saved.");
    }

    /// <summary>
    /// Configure Slack channel
    /// </summary>
    private void ConfigureSlack(AgentConfig config)
    {
        Console.WriteLine("\n=== Slack Configuration ===\n");

        if (config.Channels.Slack == null)
        {
            config.Channels.Slack = new SlackConfig();
        }

        var slack = config.Channels.Slack;

        Console.Write($"Enable Slack? [{(slack.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        slack.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && slack.Enabled);

        if (!slack.Enabled)
        {
            Console.WriteLine("Slack disabled.");
            return;
        }

        Console.Write($"Mode (socket/http) [{slack.Mode}]: ");
        var mode = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(mode))
        {
            slack.Mode = mode;
        }

        Console.Write($"Bot Token [{MaskString(slack.BotToken)}]: ");
        var botToken = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(botToken))
        {
            slack.BotToken = botToken;
        }

        Console.Write($"App Token [{MaskString(slack.AppToken)}]: ");
        var appToken = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(appToken))
        {
            slack.AppToken = appToken;
        }

        Console.WriteLine("✓ Slack configuration saved.");
    }

    /// <summary>
    /// Configure Email channel
    /// </summary>
    private void ConfigureEmail(AgentConfig config)
    {
        Console.WriteLine("\n=== Email Configuration ===\n");

        if (config.Channels.Email == null)
        {
            config.Channels.Email = new EmailConfig();
        }

        var email = config.Channels.Email;

        Console.Write($"Enable Email? [{(email.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        email.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && email.Enabled);

        if (!email.Enabled)
        {
            Console.WriteLine("Email disabled.");
            return;
        }

        Console.WriteLine("\n--- IMAP Settings (Incoming) ---");

        Console.Write($"IMAP Host [{email.ImapHost}]: ");
        var imapHost = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(imapHost))
        {
            email.ImapHost = imapHost;
        }

        Console.Write($"IMAP Port [{email.ImapPort}]: ");
        var imapPort = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(imapPort) && int.TryParse(imapPort, out var imapPortNum))
        {
            email.ImapPort = imapPortNum;
        }

        Console.Write($"IMAP Username [{email.ImapUsername}]: ");
        var imapUsername = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(imapUsername))
        {
            email.ImapUsername = imapUsername;
        }

        Console.Write($"IMAP Password [{MaskString(email.ImapPassword)}]: ");
        var imapPassword = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(imapPassword))
        {
            email.ImapPassword = imapPassword;
        }

        Console.WriteLine("\n--- SMTP Settings (Outgoing) ---");

        Console.Write($"SMTP Host [{email.SmtpHost}]: ");
        var smtpHost = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(smtpHost))
        {
            email.SmtpHost = smtpHost;
        }

        Console.Write($"SMTP Port [{email.SmtpPort}]: ");
        var smtpPort = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(smtpPort) && int.TryParse(smtpPort, out var smtpPortNum))
        {
            email.SmtpPort = smtpPortNum;
        }

        Console.Write($"SMTP Username [{email.SmtpUsername}]: ");
        var smtpUsername = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(smtpUsername))
        {
            email.SmtpUsername = smtpUsername;
        }

        Console.Write($"SMTP Password [{MaskString(email.SmtpPassword)}]: ");
        var smtpPassword = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(smtpPassword))
        {
            email.SmtpPassword = smtpPassword;
        }

        Console.Write($"From Address [{email.FromAddress}]: ");
        var fromAddress = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(fromAddress))
        {
            email.FromAddress = fromAddress;
        }

        Console.WriteLine("✓ Email configuration saved.");
    }

    /// <summary>
    /// Configure Matrix channel
    /// </summary>
    private void ConfigureMatrix(AgentConfig config)
    {
        Console.WriteLine("\n=== Matrix Configuration ===\n");

        if (config.Channels.Matrix == null)
        {
            config.Channels.Matrix = new MatrixConfig();
        }

        var matrix = config.Channels.Matrix;

        Console.Write($"Enable Matrix? [{(matrix.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        matrix.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && matrix.Enabled);

        if (!matrix.Enabled)
        {
            Console.WriteLine("Matrix disabled.");
            return;
        }

        Console.Write($"Homeserver [{matrix.Homeserver}]: ");
        var homeserver = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(homeserver))
        {
            matrix.Homeserver = homeserver;
        }

        Console.Write($"Access Token [{MaskString(matrix.AccessToken)}]: ");
        var token = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(token))
        {
            matrix.AccessToken = token;
        }

        Console.Write($"Room ID [{matrix.RoomId}]: ");
        var roomId = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(roomId))
        {
            matrix.RoomId = roomId;
        }

        Console.WriteLine("✓ Matrix configuration saved.");
    }

    /// <summary>
    /// Configure WeiXin channel
    /// </summary>
    private void ConfigureWeiXin(AgentConfig config)
    {
        Console.WriteLine("\n=== WeiXin (微信) Configuration ===\n");

        if (config.Channels.WeiXin == null)
        {
            config.Channels.WeiXin = new WeiXinConfig();
        }

        var weixin = config.Channels.WeiXin;

        Console.Write($"Enable WeiXin? [{(weixin.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        weixin.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && weixin.Enabled);

        if (!weixin.Enabled)
        {
            Console.WriteLine("WeiXin disabled.");
            return;
        }

        Console.Write($"Base URL [{weixin.BaseUrl}]: ");
        var baseUrl = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            weixin.BaseUrl = baseUrl;
        }

        Console.Write($"CDN Base URL [{weixin.CdnBaseUrl}]: ");
        var cdnBaseUrl = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(cdnBaseUrl))
        {
            weixin.CdnBaseUrl = cdnBaseUrl;
        }

        Console.Write($"Poll Timeout (seconds) [{weixin.PollTimeout}]: ");
        var pollTimeout = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(pollTimeout) && int.TryParse(pollTimeout, out var timeout))
        {
            weixin.PollTimeout = timeout;
        }

        Console.Write($"Allowed Users (comma-separated) [{string.Join(",", weixin.AllowFrom)}]: ");
        var users = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(users))
        {
            weixin.AllowFrom = users.Split(',').Select(u => u.Trim()).ToArray();
        }

        var stateDir = string.IsNullOrEmpty(weixin.StateDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nanobot", "weixin")
            : weixin.StateDir;
        Console.Write($"State Directory [{stateDir}]: ");
        var sd = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(sd))
        {
            weixin.StateDir = sd;
        }

        Console.WriteLine("\n WeiXin requires QR code authentication.");
        Console.WriteLine("  After enabling, run: nbot channels login weixin");
        Console.WriteLine("  The token will be saved automatically after login.\n");
        Console.WriteLine("✓ WeiXin configuration saved.");
    }

    /// <summary>
    /// Configure Tools (Playwright, OmniParser, PowerShell)
    /// </summary>
    private async Task ConfigureToolsAsync(
        AgentConfig config,
        string configPath,
        EnvironmentInfo envInfo,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Console.WriteLine("\n=== Tools Configuration ===\n");

            var playwrightInstalled = await IsPlaywrightInstalledAsync(cancellationToken);
            var omniInstalled = await IsOmniParserInstalledAsync(configPath);
            var powerShellInstalled = await IsPowerShellInstalledAsync(cancellationToken);
            var browserSkillAvailable = await IsBrowserSkillAvailableAsync(config, cancellationToken);
            var rpaSkillAvailable = await IsRpaSkillAvailableAsync(config, configPath, cancellationToken);

            Console.WriteLine("Available Tools:");
            Console.WriteLine($"  [1] Playwright (Browser)       [{(playwrightInstalled ? "installed" : "not installed")}]  browser skill: {(browserSkillAvailable ? "available" : "unavailable")}");
            Console.WriteLine($"  [2] OmniParser (RPA)           [{(omniInstalled ? "installed" : "not installed")}]  rpa skill: {(rpaSkillAvailable ? "available" : "unavailable")}");
            Console.WriteLine($"  [3] PowerShell Core            [{(powerShellInstalled ? "installed" : "not installed")}]");
            Console.WriteLine("  [4] Back to Main Menu");

            Console.Write("\nSelect tool to configure: ");
            var key = Console.ReadKey(true);
            Console.WriteLine(key.KeyChar);

            var option = NormalizeInputChar(key.KeyChar);

            switch (option)
            {
                case '1':
                    await ConfigurePlaywrightAsync(config, configPath, cancellationToken);
                    break;
                case '2':
                    await ConfigureOmniParserAsync(config, configPath, cancellationToken);
                    break;
                case '3':
                    await ConfigurePowerShellAsync(cancellationToken);
                    break;
                case '4':
                    return;
                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }
        }
    }

    /// <summary>
    /// Configure OmniParser with full sub-menu
    /// </summary>
    private async Task ConfigureOmniParserAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        while (true)
        {
            var isInstalled = await IsOmniParserInstalledAsync(configPath);
            var rpaSkillAvailable = await IsRpaSkillAvailableAsync(config, configPath, cancellationToken);

            Console.WriteLine("\n=== OmniParser Setup ===\n");
            Console.WriteLine($"Status: {(isInstalled ? "Installed" : "Not Installed")}");
            if (isInstalled && config.Rpa != null)
            {
                Console.WriteLine($"  Install Path: {config.Rpa.InstallPath}");
                Console.WriteLine($"  Service Port: {config.Rpa.ServicePort}");
                Console.WriteLine($"  RPA Skill: {(rpaSkillAvailable ? "available" : "disabled")}");
            }

            Console.WriteLine();
            Console.WriteLine("  [1] Install OmniParser");
            Console.WriteLine("  [2] Reinstall OmniParser");
            if (isInstalled)
            {
                Console.WriteLine("  [3] Configure RPA settings");
                Console.WriteLine("  [4] Enable/Disable RPA tools");
                Console.WriteLine("  [5] Back");
            }
            else
            {
                Console.WriteLine("  [3] Back");
            }

            Console.Write("\nSelect option: ");
            var key = Console.ReadKey(true);
            Console.WriteLine(key.KeyChar);

            var option = NormalizeInputChar(key.KeyChar);

            if (!isInstalled && option == '3')
                return;

            switch (option)
            {
                case '1':
                    if (!isInstalled)
                    {
                        await InstallOmniParserAsync(config, configPath, false, cancellationToken);
                    }
                    break;
                case '2':
                    await InstallOmniParserAsync(config, configPath, false, cancellationToken);
                    break;
                case '3':
                    if (isInstalled)
                    {
                        ConfigureRpaSettings(config, configPath, cancellationToken);
                    }
                    break;
                case '4':
                    if (isInstalled)
                    {
                        ConfigureRpaEnabled(config, configPath, cancellationToken);
                    }
                    break;
                case '5':
                    return;
                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }
        }
    }

    /// <summary>
    /// Configure RPA enabled/disabled state
    /// </summary>
    private void ConfigureRpaEnabled(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== RPA Tools Enable/Disable ===\n");

        if (config.Rpa == null)
        {
            config.Rpa = new RpaToolsConfig { Enabled = true };
        }

        Console.Write($"Enable RPA tools? [{(config.Rpa.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.Rpa.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && config.Rpa.Enabled);

        Console.WriteLine($"✓ RPA tools {(config.Rpa.Enabled ? "enabled" : "disabled")}.");
    }

    /// <summary>
    /// Configure RPA settings
    /// </summary>
    private void ConfigureRpaSettings(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== RPA Settings ===\n");

        if (config.Rpa == null)
        {
            var omniparserPath = Path.Combine(GetConfigDir(configPath), "omniparser");
            config.Rpa = new RpaToolsConfig
            {
                Enabled = true,
                InstallPath = omniparserPath,
                ServicePort = 18999,
                AutoStartService = true
            };
        }

        Console.Write($"Service Port [{config.Rpa.ServicePort}]: ");
        var port = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var portNum))
        {
            config.Rpa.ServicePort = portNum;
        }

        Console.Write($"Auto-start service? [{(config.Rpa.AutoStartService ? "Y/n" : "y/N")}]: ");
        var autoStart = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.Rpa.AutoStartService = autoStart == "y" || autoStart == "yes" || (string.IsNullOrEmpty(autoStart) && config.Rpa.AutoStartService);

        Console.Write($"Screenshot Save Path (press Enter to skip) [{config.Rpa.ScreenshotPath ?? "(null)"}]: ");
        var screenshotPath = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(screenshotPath))
        {
            config.Rpa.ScreenshotPath = screenshotPath;
        }
        else if (string.IsNullOrEmpty(screenshotPath) && !string.IsNullOrEmpty(config.Rpa.ScreenshotPath))
        {
            // Keep existing
        }
        else
        {
            config.Rpa.ScreenshotPath = null;
        }

        Console.WriteLine("\nScreenshot Optimization:");
        if (config.Rpa.ScreenshotOptimization == null)
        {
            config.Rpa.ScreenshotOptimization = new ScreenshotOptimizationConfig();
        }

        Console.Write($"  Max Dimension (pixels) [{config.Rpa.ScreenshotOptimization.MaxDimension}]: ");
        var maxDim = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(maxDim) && int.TryParse(maxDim, out var maxDimNum))
        {
            config.Rpa.ScreenshotOptimization.MaxDimension = maxDimNum;
        }

        Console.Write($"  JPEG Quality (0-100) [{config.Rpa.ScreenshotOptimization.JpegQuality}]: ");
        var quality = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(quality) && int.TryParse(quality, out var qualityNum))
        {
            config.Rpa.ScreenshotOptimization.JpegQuality = Math.Clamp(qualityNum, 0, 100);
        }

        Console.WriteLine("✓ RPA settings saved.");
    }

    /// <summary>
    /// Configure Playwright with enhanced sub-menu
    /// </summary>
    private async Task ConfigurePlaywrightAsync(
        AgentConfig config,
        string configPath,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var isInstalled = await IsPlaywrightInstalledAsync(cancellationToken);
            var browserSkillAvailable = await IsBrowserSkillAvailableAsync(config, cancellationToken);

            Console.WriteLine("\n=== Playwright Setup ===\n");
            Console.WriteLine($"Status: {(isInstalled ? "Installed" : "Not Installed")}");
            if (isInstalled)
            {
                Console.WriteLine($"  Browser Tools: {(config.Browser?.Enabled == true ? "enabled" : "disabled")}");
                Console.WriteLine($"  browser skill: {(browserSkillAvailable ? "available" : "unavailable")}");
            }

            Console.WriteLine();
            Console.WriteLine("  [1] Install Playwright browsers");
            if (isInstalled)
            {
                Console.WriteLine("  [2] Reinstall Playwright browsers");
                Console.WriteLine("  [3] Enable/Disable browser tools");
                Console.WriteLine("  [4] Back");
            }
            else
            {
                Console.WriteLine("  [2] Back");
            }

            Console.Write("\nSelect option: ");
            var key = Console.ReadKey(true);
            Console.WriteLine(key.KeyChar);

            var option = NormalizeInputChar(key.KeyChar);

            if (!isInstalled && option == '2')
                return;

            switch (option)
            {
                case '1':
                    if (!isInstalled)
                    {
                        await InstallPlaywrightBrowsersAsync(config, configPath, false, cancellationToken);
                    }
                    break;
                case '2':
                    if (isInstalled)
                    {
                        await InstallPlaywrightBrowsersAsync(config, configPath, false, cancellationToken);
                    }
                    break;
                case '3':
                    if (isInstalled)
                    {
                        ConfigureBrowserEnabled(config, configPath, cancellationToken);
                    }
                    break;
                case '4':
                    return;
                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }
        }
    }

    /// <summary>
    /// Configure browser enabled/disabled state
    /// </summary>
    private void ConfigureBrowserEnabled(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== Browser Tools Enable/Disable ===\n");

        if (config.Browser == null)
        {
            config.Browser = new BrowserToolsConfig { Enabled = true };
        }

        Console.Write($"Enable browser tools? [{(config.Browser.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.Browser.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && config.Browser.Enabled);

        Console.WriteLine($"✓ Browser tools {(config.Browser.Enabled ? "enabled" : "disabled")}.");
    }

    /// <summary>
    /// Check if browser skill is available
    /// </summary>
    private async Task<bool> IsBrowserSkillAvailableAsync(AgentConfig config, CancellationToken cancellationToken)
    {
        if (config.Browser?.Enabled != true)
            return false;
        return await IsPlaywrightInstalledAsync(cancellationToken);
    }

    /// <summary>
    /// Check if RPA skill is available
    /// </summary>
    private async Task<bool> IsRpaSkillAvailableAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        if (config.Rpa == null || !config.Rpa.Enabled)
            return false;
        return await IsOmniParserInstalledAsync(configPath);
    }

    /// <summary>
    /// Check if OmniParser is installed
    /// </summary>
    private async Task<bool> IsOmniParserInstalledAsync(string configPath)
    {
        var omniparserPath = Path.Combine(GetConfigDir(configPath), "omniparser");
        var venvPath = Path.Combine(omniparserPath, "venv");
        return await Task.FromResult(Directory.Exists(venvPath));
    }

    /// <summary>
    /// Check if Playwright is installed
    /// </summary>
    private async Task<bool> IsPlaywrightInstalledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var installer = new PlaywrightInstaller();
            return await installer.IsInstalledAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Configure PowerShell
    /// </summary>
    private async Task ConfigurePowerShellAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== PowerShell Configuration ===\n");

        var isInstalled = await IsPowerShellInstalledAsync(cancellationToken);

        if (isInstalled)
        {
            Console.WriteLine("✓ PowerShell Core is already installed.");
            var path = await GetPowerShellPathAsync(cancellationToken);
            Console.WriteLine($"  Location: {path}");
        }
        else
        {
            Console.WriteLine("PowerShell Core is not installed.");
            Console.WriteLine("Starting installation (this may take a few minutes)...\n");

            var installer = new PowerShellInstaller();
            var installed = await installer.InstallAsync(cancellationToken);

            if (installed)
            {
                Console.WriteLine("✓ PowerShell Core installed successfully");
            }
            else
            {
                Console.WriteLine("✗ Failed to install PowerShell Core automatically.");
                Console.WriteLine("  Please install manually from: https://aka.ms/powershell");
            }
        }
    }

    /// <summary>
    /// Check if PowerShell is installed
    /// </summary>
    private async Task<bool> IsPowerShellInstalledAsync(CancellationToken cancellationToken)
    {
        var path = await GetPowerShellPathAsync(cancellationToken);
        return !string.IsNullOrEmpty(path);
    }

    /// <summary>
    /// Get PowerShell path
    /// </summary>
    private async Task<string?> GetPowerShellPathAsync(CancellationToken cancellationToken)
    {
        var installer = new PowerShellInstaller();
        return await installer.GetPowerShellPathAsync(cancellationToken);
    }

    /// <summary>
    /// Configure Workspace
    /// </summary>
    private void ConfigureWorkspace(AgentConfig config)
    {
        Console.WriteLine("\n=== Workspace Configuration ===\n");

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

    /// <summary>
    /// Configure Memory settings
    /// </summary>
    private async Task ConfigureMemoryAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== Memory Configuration ===\n");

        if (config.Memory == null)
        {
            config.Memory = new MemoryConfig();
        }

        Console.Write($"Enable memory? [{(config.Memory.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.Memory.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && config.Memory.Enabled);

        Console.Write($"Memory Window (messages) [{config.Memory.MemoryWindow}]: ");
        var window = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(window) && int.TryParse(window, out var windowNum))
        {
            config.Memory.MemoryWindow = Math.Max(1, windowNum);
        }

        Console.Write($"Max History Entries [{config.Memory.MaxHistoryEntries}]: ");
        var maxHistory = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(maxHistory) && int.TryParse(maxHistory, out var maxHistoryNum))
        {
            config.Memory.MaxHistoryEntries = Math.Max(0, maxHistoryNum);
        }

        Console.Write($"Enable HISTORY.md (grep-searchable archive)? [{(config.Memory.EnableHistory ? "Y/n" : "y/N")}]: ");
        var enableHistory = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.Memory.EnableHistory = enableHistory == "y" || enableHistory == "yes" || (string.IsNullOrEmpty(enableHistory) && config.Memory.EnableHistory);

        Console.Write($"Max Instruction Chars (0=unlimited) [{config.Memory.MaxInstructionChars}]: ");
        var maxChars = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(maxChars) && int.TryParse(maxChars, out var maxCharsNum))
        {
            config.Memory.MaxInstructionChars = Math.Max(0, maxCharsNum);
        }

        Console.WriteLine("✓ Memory configuration saved.");
        await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
    }

    /// <summary>
    /// Configure Security settings
    /// </summary>
    private void ConfigureSecurity(AgentConfig config)
    {
        Console.WriteLine("\n=== Security Configuration ===\n");

        Console.Write($"Restrict to workspace? [{(config.Security.RestrictToWorkspace ? "Y/n" : "y/N")}]: ");
        var restrict = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.Security.RestrictToWorkspace = restrict == "y" || restrict == "yes" || (string.IsNullOrEmpty(restrict) && config.Security.RestrictToWorkspace);

        Console.Write($"Shell Timeout (seconds) [{config.Security.ShellTimeout}]: ");
        var timeout = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(timeout) && int.TryParse(timeout, out var timeoutNum))
        {
            config.Security.ShellTimeout = Math.Max(1, timeoutNum);
        }

        Console.WriteLine("\nAllowed Directories (comma-separated, empty = all):");
        var currentDirs = config.Security.AllowedDirs != null && config.Security.AllowedDirs.Count > 0
            ? string.Join(", ", config.Security.AllowedDirs)
            : "(all allowed)";
        Console.Write($"  [{currentDirs}]: ");
        var dirs = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(dirs))
        {
            config.Security.AllowedDirs = dirs.Split(',').Select(d => d.Trim()).Where(d => !string.IsNullOrEmpty(d)).ToList();
        }

        Console.WriteLine("\nDeny Command Patterns (comma-separated, empty = none):");
        var currentDeny = config.Security.DenyCommandPatterns != null && config.Security.DenyCommandPatterns.Count > 0
            ? string.Join(", ", config.Security.DenyCommandPatterns)
            : "(none)";
        Console.Write($"  [{currentDeny}]: ");
        var deny = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(deny))
        {
            config.Security.DenyCommandPatterns = deny.Split(',').Select(d => d.Trim()).Where(d => !string.IsNullOrEmpty(d)).ToList();
        }

        Console.WriteLine("✓ Security configuration saved.");
    }

    /// <summary>
    /// Configure MCP servers
    /// </summary>
    private async Task ConfigureMcpAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        while (true)
        {
            Console.WriteLine("\n=== MCP Configuration ===\n");

            if (config.Mcp == null)
            {
                config.Mcp = new McpConfig();
            }

            var servers = config.Mcp.Servers;
            if (servers.Count == 0)
            {
                Console.WriteLine("No MCP servers configured.\n");
            }
            else
            {
                Console.WriteLine("Configured Servers:");
                foreach (var (name, server) in servers)
                {
                    Console.WriteLine($"  • {name}: {server.Command} {string.Join(" ", server.Args)}");
                }
                Console.WriteLine();
            }

            Console.WriteLine("  [1] Add MCP server");
            Console.WriteLine("  [2] Edit MCP server");
            Console.WriteLine("  [3] Delete MCP server");
            Console.WriteLine("  [4] Back to Main Menu");

            Console.Write("\nSelect option: ");
            var key = Console.ReadKey(true);
            Console.WriteLine(key.KeyChar);

            var option = NormalizeInputChar(key.KeyChar);

            switch (option)
            {
                case '1':
                    AddMcpServer(config, configPath, cancellationToken);
                    break;
                case '2':
                    EditMcpServer(config, configPath, cancellationToken);
                    break;
                case '3':
                    DeleteMcpServer(config, configPath, cancellationToken);
                    break;
                case '4':
                    return;
                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }
        }
    }

    private void AddMcpServer(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== Add MCP Server ===\n");

        Console.Write("Server name: ");
        var name = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Invalid server name.");
            return;
        }

        if (config.Mcp!.Servers.ContainsKey(name))
        {
            Console.WriteLine($"Server '{name}' already exists.");
            return;
        }

        Console.Write("Command (e.g., npx, python): ");
        var command = Console.ReadLine()?.Trim();

        Console.Write("Arguments (comma-separated, e.g., -m, mcp_server): ");
        var argsInput = Console.ReadLine()?.Trim();
        var args = string.IsNullOrWhiteSpace(argsInput)
            ? new List<string>()
            : argsInput.Split(',').Select(a => a.Trim()).ToList();

        var server = new McpServerConfig
        {
            Command = command ?? "",
            Args = args
        };

        config.Mcp.Servers[name] = server;
        Console.WriteLine($"✓ MCP server '{name}' added.");
        _ = ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
    }

    private void EditMcpServer(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        if (config.Mcp!.Servers.Count == 0)
        {
            Console.WriteLine("No MCP servers to edit.");
            return;
        }

        var serverNames = config.Mcp.Servers.Keys.ToList();
        Console.WriteLine("Select server to edit:");
        for (var i = 0; i < serverNames.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {serverNames[i]}");
        }
        Console.Write("\nEnter number or name: ");
        var input = Console.ReadLine()?.Trim();

        string? selectedName = null;
        if (int.TryParse(input, out var index) && index > 0 && index <= serverNames.Count)
        {
            selectedName = serverNames[index - 1];
        }
        else if (!string.IsNullOrWhiteSpace(input) && config.Mcp.Servers.ContainsKey(input))
        {
            selectedName = input;
        }

        if (selectedName == null)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        var server = config.Mcp.Servers[selectedName];

        Console.Write($"\nCommand [{server.Command}]: ");
        var command = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(command))
        {
            server.Command = command;
        }

        Console.Write($"Arguments (comma-separated) [{string.Join(", ", server.Args)}]: ");
        var argsInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(argsInput))
        {
            server.Args = argsInput.Split(',').Select(a => a.Trim()).ToList();
        }

        Console.Write($"Tool Timeout (seconds) [{server.ToolTimeout}]: ");
        var timeout = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(timeout) && int.TryParse(timeout, out var timeoutNum))
        {
            server.ToolTimeout = Math.Max(1, timeoutNum);
        }

        Console.WriteLine($"✓ MCP server '{selectedName}' updated.");
        _ = ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
    }

    private void DeleteMcpServer(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        if (config.Mcp!.Servers.Count == 0)
        {
            Console.WriteLine("No MCP servers to delete.");
            return;
        }

        var serverNames = config.Mcp.Servers.Keys.ToList();
        Console.WriteLine("Select server to delete:");
        for (var i = 0; i < serverNames.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {serverNames[i]}");
        }
        Console.Write("\nEnter number or name: ");
        var input = Console.ReadLine()?.Trim();

        string? selectedName = null;
        if (int.TryParse(input, out var index) && index > 0 && index <= serverNames.Count)
        {
            selectedName = serverNames[index - 1];
        }
        else if (!string.IsNullOrWhiteSpace(input) && config.Mcp.Servers.ContainsKey(input))
        {
            selectedName = input;
        }

        if (selectedName == null)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        Console.Write($"Delete MCP server '{selectedName}'? [y/N]: ");
        var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (confirm == "y" || confirm == "yes")
        {
            config.Mcp.Servers.Remove(selectedName);
            Console.WriteLine($"✓ MCP server '{selectedName}' deleted.");
            _ = ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
        }
        else
        {
            Console.WriteLine("Cancelled.");
        }
    }

    /// <summary>
    /// Configure Heartbeat settings
    /// </summary>
    private async Task ConfigureHeartbeatAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== Heartbeat Configuration ===\n");

        if (config.Heartbeat == null)
        {
            config.Heartbeat = new HeartbeatConfig();
        }

        Console.Write($"Enable heartbeat? [{(config.Heartbeat.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.Heartbeat.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && config.Heartbeat.Enabled);

        if (!config.Heartbeat.Enabled)
        {
            Console.WriteLine("✓ Heartbeat disabled.");
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            return;
        }

        Console.Write($"Interval (seconds) [{config.Heartbeat.IntervalSeconds}]: ");
        var interval = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(interval) && int.TryParse(interval, out var intervalNum))
        {
            config.Heartbeat.IntervalSeconds = Math.Max(10, intervalNum);
        }

        Console.Write($"Heartbeat Message [{config.Heartbeat.Message ?? "(null)"}]: ");
        var message = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(message))
        {
            config.Heartbeat.Message = message;
        }

        Console.WriteLine("✓ Heartbeat configuration saved.");
        await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
    }

    /// <summary>
    /// Configure WebUI settings
    /// </summary>
    private async Task ConfigureWebUiAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        while (true)
        {
            Console.WriteLine("\n=== WebUI Configuration ===\n");

            Console.WriteLine($"  Enabled: {(config.WebUI.Enabled ? "yes" : "no")}");
            Console.WriteLine($"  Host: {config.WebUI.Server.Host}");
            Console.WriteLine($"  Port: {config.WebUI.Server.Port}");
            Console.WriteLine($"  Auth Mode: {config.WebUI.Auth.Mode}");
            Console.WriteLine($"  CORS Allowed Origins: {config.WebUI.Cors.AllowedOrigins.Count}");
            Console.WriteLine($"  HTTPS: {(config.WebUI.Security.EnableHttps ? "enabled" : "disabled")}");
            Console.WriteLine($"  File Upload: {(config.WebUI.Features.FileUpload ? "enabled" : "disabled")}");
            Console.WriteLine();

            Console.WriteLine("  [1] Enable/Disable WebUI");
            Console.WriteLine("  [2] Server settings (host/port)");
            Console.WriteLine("  [3] Auth settings");
            Console.WriteLine("  [4] CORS settings");
            Console.WriteLine("  [5] Security settings");
            Console.WriteLine("  [6] Features settings");
            Console.WriteLine("  [7] Back to Main Menu");

            Console.Write("\nSelect option: ");
            var key = Console.ReadKey(true);
            Console.WriteLine(key.KeyChar);

            var option = NormalizeInputChar(key.KeyChar);

            switch (option)
            {
                case '1':
                    ToggleWebUiEnabled(config);
                    break;
                case '2':
                    ConfigureWebUiServer(config);
                    break;
                case '3':
                    ConfigureWebUiAuth(config);
                    break;
                case '4':
                    ConfigureWebUiCors(config);
                    break;
                case '5':
                    ConfigureWebUiSecurity(config);
                    break;
                case '6':
                    ConfigureWebUiFeatures(config);
                    break;
                case '7':
                    return;
                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }

            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
        }
    }

    private void ToggleWebUiEnabled(AgentConfig config)
    {
        Console.WriteLine("\n=== WebUI Enable/Disable ===\n");
        Console.Write($"Enable WebUI? [{(config.WebUI.Enabled ? "Y/n" : "y/N")}]: ");
        var enable = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.WebUI.Enabled = enable == "y" || enable == "yes" || (string.IsNullOrEmpty(enable) && config.WebUI.Enabled);
        Console.WriteLine($"✓ WebUI {(config.WebUI.Enabled ? "enabled" : "disabled")}.");
    }

    private void ConfigureWebUiServer(AgentConfig config)
    {
        Console.WriteLine("\n=== WebUI Server Settings ===\n");

        Console.Write($"Host [{config.WebUI.Server.Host}]: ");
        var host = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(host))
        {
            config.WebUI.Server.Host = host;
        }

        Console.Write($"Port [{config.WebUI.Server.Port}]: ");
        var port = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var portNum))
        {
            config.WebUI.Server.Port = portNum;
        }

        Console.WriteLine("✓ Server settings saved.");
    }

    private void ConfigureWebUiAuth(AgentConfig config)
    {
        Console.WriteLine("\n=== WebUI Auth Settings ===\n");

        Console.Write($"Auth Mode (token/password) [{config.WebUI.Auth.Mode}]: ");
        var mode = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(mode))
        {
            config.WebUI.Auth.Mode = mode;
        }

        Console.Write("New Token (press Enter to keep current): ");
        var token = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(token))
        {
            config.WebUI.Auth.Token = token;
        }

        Console.Write($"Allow Localhost? [{(config.WebUI.Auth.AllowLocalhost ? "Y/n" : "y/N")}]: ");
        var allowLocal = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.WebUI.Auth.AllowLocalhost = allowLocal == "y" || allowLocal == "yes" || (string.IsNullOrEmpty(allowLocal) && config.WebUI.Auth.AllowLocalhost);

        Console.WriteLine("✓ Auth settings saved.");
    }

    private void ConfigureWebUiCors(AgentConfig config)
    {
        Console.WriteLine("\n=== WebUI CORS Settings ===\n");

        Console.Write($"Allow Any Origin? [{(config.WebUI.Cors.AllowAnyOrigin ? "Y/n" : "y/N")}]: ");
        var anyOrigin = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.WebUI.Cors.AllowAnyOrigin = anyOrigin == "y" || anyOrigin == "yes" || (string.IsNullOrEmpty(anyOrigin) && config.WebUI.Cors.AllowAnyOrigin);

        if (!config.WebUI.Cors.AllowAnyOrigin)
        {
            var currentOrigins = config.WebUI.Cors.AllowedOrigins.Count > 0
                ? string.Join(", ", config.WebUI.Cors.AllowedOrigins)
                : "(none)";
            Console.Write($"Allowed Origins (comma-separated) [{currentOrigins}]: ");
            var origins = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(origins))
            {
                config.WebUI.Cors.AllowedOrigins = origins.Split(',').Select(o => o.Trim()).Where(o => !string.IsNullOrEmpty(o)).ToList();
            }
        }

        Console.WriteLine("✓ CORS settings saved.");
    }

    private void ConfigureWebUiSecurity(AgentConfig config)
    {
        Console.WriteLine("\n=== WebUI Security Settings ===\n");

        Console.Write($"Enable HTTPS? [{(config.WebUI.Security.EnableHttps ? "Y/n" : "y/N")}]: ");
        var https = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.WebUI.Security.EnableHttps = https == "y" || https == "yes" || (string.IsNullOrEmpty(https) && config.WebUI.Security.EnableHttps);

        Console.Write($"Enable Rate Limit? [{(config.WebUI.Security.EnableRateLimit ? "Y/n" : "y/N")}]: ");
        var rateLimit = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.WebUI.Security.EnableRateLimit = rateLimit == "y" || rateLimit == "yes" || (string.IsNullOrEmpty(rateLimit) && config.WebUI.Security.EnableRateLimit);

        if (config.WebUI.Security.EnableRateLimit)
        {
            Console.Write($"Max Requests Per Minute [{config.WebUI.Security.MaxRequestsPerMinute}]: ");
            var maxReq = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(maxReq) && int.TryParse(maxReq, out var maxReqNum))
            {
                config.WebUI.Security.MaxRequestsPerMinute = Math.Max(1, maxReqNum);
            }
        }

        Console.WriteLine("✓ Security settings saved.");
    }

    private void ConfigureWebUiFeatures(AgentConfig config)
    {
        Console.WriteLine("\n=== WebUI Features Settings ===\n");

        Console.Write($"Enable File Upload? [{(config.WebUI.Features.FileUpload ? "Y/n" : "y/N")}]: ");
        var fileUpload = Console.ReadLine()?.Trim().ToLowerInvariant();
        config.WebUI.Features.FileUpload = fileUpload == "y" || fileUpload == "yes" || (string.IsNullOrEmpty(fileUpload) && config.WebUI.Features.FileUpload);

        Console.Write($"Max File Size [{config.WebUI.Features.MaxFileSize}]: ");
        var maxSize = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(maxSize))
        {
            config.WebUI.Features.MaxFileSize = maxSize;
        }

        var currentTypes = config.WebUI.Features.AllowedFileTypes.Count > 0
            ? string.Join(", ", config.WebUI.Features.AllowedFileTypes)
            : "(all)";
        Console.Write($"Allowed File Types (comma-separated) [{currentTypes}]: ");
        var types = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(types))
        {
            config.WebUI.Features.AllowedFileTypes = types.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
        }

        Console.WriteLine("✓ Features settings saved.");
    }

    /// <summary>
    /// Configure Agent Settings (name, timezone)
    /// </summary>
    private async Task ConfigureAgentSettingsAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== Agent Settings ===\n");

        Console.Write($"Agent Name [{config.Name}]: ");
        var name = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            config.Name = name;
        }

        Console.Write($"Timezone (IANA format, e.g., Asia/Shanghai, UTC) [{config.Timezone ?? "(not set)"}]: ");
        var timezone = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(timezone))
        {
            config.Timezone = timezone;
        }

        Console.WriteLine("✓ Agent settings saved.");
        await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
    }

    /// <summary>
    /// Save configuration
    /// </summary>
    private async Task SaveConfigAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        var configDir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
        Console.WriteLine($"\n✓ Configuration saved to {configPath}");
    }

    /// <summary>
    /// Start Agent Mode
    /// </summary>
    private async Task StartAgentModeAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        Console.WriteLine("\n🐈 Starting Agent Mode...\n");

        // Get the path to the current executable
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
        {
            exePath = "nbot";
        }

        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "agent",
                    UseShellExecute = false,
                    CreateNoWindow = false
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start Agent Mode: {ex.Message}");
            Console.WriteLine("You can start it manually by running: nbot agent");
        }
    }

    /// <summary>
    /// Start Web UI Mode
    /// </summary>
    private async Task StartWebUIModeAsync(AgentConfig config, string configPath, CancellationToken cancellationToken)
    {
        Console.WriteLine("\n🐈 Starting Web UI Mode...\n");

        // Get the path to the current executable
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
        {
            exePath = "nbot";
        }

        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "webui",
                    UseShellExecute = false,
                    CreateNoWindow = false
                }
            };

            process.Start();
            Console.WriteLine($"Web UI started. Access it at: http://{config.WebUI.Server.Host}:{config.WebUI.Server.Port}");
            Console.WriteLine("Press any key to stop the Web UI...");
            Console.ReadKey(true);

            try
            {
                process.Kill();
                await process.WaitForExitAsync(cancellationToken);
            }
            catch { /* Ignore */ }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start Web UI Mode: {ex.Message}");
            Console.WriteLine("You can start it manually by running: nbot webui");
        }
    }

    /// <summary>
    /// Install Playwright browsers
    /// </summary>
    private static async Task<bool> InstallPlaywrightBrowsersAsync(
        AgentConfig config,
        string configPath,
        bool nonInteractive,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("\n=== Browser Tools Setup ===\n");

        var powerShellInstaller = new PowerShellInstaller();
        var installer = new PlaywrightInstaller(powerShellInstaller: powerShellInstaller);

        // First check if Playwright browsers are already installed
        Console.WriteLine("Checking Playwright browser installation...");
        if (await installer.IsInstalledAsync(cancellationToken))
        {
            Console.WriteLine("✓ Playwright browsers already installed");
            config.Browser = new BrowserToolsConfig { Enabled = true };
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            return true;
        }

        // In non-interactive mode, skip installation silently
        if (nonInteractive)
        {
            config.Browser = new BrowserToolsConfig { Enabled = false };
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            return false;
        }

        // Check PowerShell availability and install if needed
        Console.WriteLine("Checking PowerShell availability...");
        var pwshPath = await powerShellInstaller.GetPowerShellPathAsync(cancellationToken);

        if (string.IsNullOrEmpty(pwshPath))
        {
            Console.WriteLine("PowerShell Core (pwsh) is required for Playwright installation.");
            Console.WriteLine("Installing PowerShell Core (this may take a few minutes)...");

            var psInstalled = await powerShellInstaller.InstallAsync(cancellationToken);

            if (!psInstalled)
            {
                Console.WriteLine("✗ Failed to install PowerShell Core automatically.");
                Console.WriteLine("  Please install manually from: https://aka.ms/powershell");
                config.Browser = new BrowserToolsConfig { Enabled = false };
                await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                return false;
            }

            Console.WriteLine("✓ PowerShell Core installed successfully");
        }
        else
        {
            Console.WriteLine("✓ PowerShell Core found");
        }

        // Now install Playwright browsers
        Console.WriteLine("\nInstalling Playwright browsers (this may take a few minutes)...");
        Console.WriteLine("  Installing Chromium...");

        try
        {
            var success = await installer.InstallAsync(new[] { "chromium" }, cancellationToken);
            if (success)
            {
                Console.WriteLine("✓ Playwright browsers installed successfully");
                config.Browser = new BrowserToolsConfig { Enabled = true };
                await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                return true;
            }
            else
            {
                var errorMessage = installer.GetStatusMessage();
                Console.WriteLine("✗ Failed to install Playwright browsers automatically.");

                // Check for platform not supported errors
                if (errorMessage.Contains("does not support") || errorMessage.Contains("unsupported") || errorMessage.Contains("operating system may not be supported"))
                {
                    Console.WriteLine("\n  Note: Your operating system may not be officially supported by Playwright.");
                    Console.WriteLine("  You can try one of the following:");
                    Console.WriteLine("    1. Use Docker with a supported Linux distribution (Ubuntu 20.04/22.04/24.04)");
                    Console.WriteLine("    2. Use WSL2 if on Windows");
                }
                else
                {
                    Console.WriteLine("  Please install manually:");
                    Console.WriteLine("    dotnet tool install --global Microsoft.Playwright.CLI");
                    Console.WriteLine("    playwright install chromium");
                }
                config.Browser = new BrowserToolsConfig { Enabled = false };
                await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error during Playwright browser installation: {ex.Message}");
            Console.WriteLine("  Please install manually:");
            Console.WriteLine("    dotnet tool install --global Microsoft.Playwright.CLI");
            Console.WriteLine("    playwright install chromium");
            config.Browser = new BrowserToolsConfig { Enabled = false };
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            return false;
        }
    }

    /// <summary>
    /// Apply non-interactive options
    /// </summary>
    private static void ApplyNonInteractiveOptions(
        AgentConfig config,
        string? provider,
        string? model,
        string? apiKey,
        string? apiBase,
        string? workspace)
    {
        // 只有当用户提供了 LLM 相关参数时，才创建 default profile
        var hasLlmConfig = !string.IsNullOrEmpty(provider) ||
                           !string.IsNullOrEmpty(model) ||
                           !string.IsNullOrEmpty(apiKey) ||
                           !string.IsNullOrEmpty(apiBase);

        if (!hasLlmConfig)
        {
            // 用户没有提供任何 LLM 配置，保持 llm 为空
            if (!string.IsNullOrEmpty(workspace))
            {
                config.Workspace.Path = workspace;
            }
            return;
        }

        // 用户提供了 LLM 配置，创建或更新 default profile
        var profileName = string.IsNullOrEmpty(config.Llm.DefaultProfile) ? "default" : config.Llm.DefaultProfile;
        if (!config.Llm.Profiles.ContainsKey(profileName))
        {
            config.Llm.Profiles[profileName] = new LlmProfile { Name = profileName };
            config.Llm.DefaultProfile = profileName;
        }
        var profile = config.Llm.Profiles[profileName];

        // 如果提供了 provider，才进行配置
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

    private async Task ConfigureLlmProfilesAsync(AgentConfig config, CancellationToken cancellationToken)
    {
        Console.WriteLine("=== LLM Profile Configuration ===\n");
        Console.WriteLine("You can configure multiple LLM profiles for different use cases.\n");

        var service = new LlmProfileConfigService();

        var hasDefaultProfile = config.Llm.Profiles.Count > 0;
        if (!hasDefaultProfile)
        {
            Console.WriteLine("No profiles configured yet. Let's create the default profile.\n");
            var profileName = string.IsNullOrEmpty(config.Llm.DefaultProfile) ? "default" : config.Llm.DefaultProfile;
            await service.ConfigureProfileInteractiveAsync(config, profileName, cancellationToken);
            config.Llm.DefaultProfile = profileName;
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

        await InitializeWorkspaceAsync(configPath, workspacePath, cancellationToken);

        // Install Playwright browsers (if not already installed)
        await InstallPlaywrightBrowsersAsync(config, configPath, nonInteractive: true, cancellationToken);

        // Install OmniParser (if not already installed)
        await InstallOmniParserAsync(config, configPath, nonInteractive: true, cancellationToken);

        Console.WriteLine("\n🐈 nbot is ready!");
        PrintNextSteps(config, configPath);
    }

    private static async Task<bool> InstallOmniParserAsync(
        AgentConfig config,
        string configPath,
        bool nonInteractive,
        CancellationToken ct)
    {
        Console.WriteLine("\n=== RPA Vision Setup (OmniParser) ===\n");
        Console.WriteLine("OmniParser provides AI-powered screen parsing for RPA tools.");
        Console.WriteLine("Requirements: Python 3.10+, ~4GB disk space for models");

        // Check Python installation
        var pythonVersion = await GetPythonVersionAsync(ct);
        if (string.IsNullOrEmpty(pythonVersion))
        {
            Console.WriteLine("Python not found.");
            if (nonInteractive)
            {
                config.Rpa = new RpaToolsConfig { Enabled = false };
                await ConfigurationLoader.SaveAsync(configPath, config, ct);
                return false;
            }

            Console.Write("Install Python via Homebrew? [Y/n]: ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response == "n")
            {
                config.Rpa = new RpaToolsConfig { Enabled = false };
                return false;
            }

            await InstallPythonViaHomebrewAsync(ct);
            pythonVersion = await GetPythonVersionAsync(ct);
        }

        Console.WriteLine($"✓ Python {pythonVersion} found");

        // Create OmniParser directory
        var omniparserPath = Path.Combine(GetConfigDir(configPath), "omniparser");
        Directory.CreateDirectory(omniparserPath);

        // Check if already installed
        var venvPath = Path.Combine(omniparserPath, "venv");
        if (Directory.Exists(venvPath))
        {
            Console.WriteLine("✓ OmniParser already installed");
            config.Rpa = new RpaToolsConfig
            {
                Enabled = true,
                InstallPath = omniparserPath,
                ServicePort = 18999,
                AutoStartService = true
            };
            await ConfigurationLoader.SaveAsync(configPath, config, ct);
            return true;
        }

        // Create virtual environment and install dependencies
        Console.WriteLine("Creating Python virtual environment...");
        await RunShellCommandAsync("python3 -m venv \"" + venvPath + "\"", omniparserPath, ct);

        Console.WriteLine("Installing OmniParser dependencies...");
        var pipPath = Path.Combine(venvPath, "bin", "pip");
        var requirementsPath = Path.Combine(omniparserPath, "requirements.txt");

        if (!File.Exists(requirementsPath))
        {
            await File.WriteAllTextAsync(requirementsPath, GetEmbeddedRequirements(), ct);
        }

        await RunShellCommandAsync("\"" + pipPath + "\" install -r \"" + requirementsPath + "\"", omniparserPath, ct);

        // Copy server script
        var serverScriptPath = Path.Combine(omniparserPath, "server.py");
        if (!File.Exists(serverScriptPath))
        {
            await File.WriteAllTextAsync(serverScriptPath, GetEmbeddedServerScript(), ct);
        }

        // Download model weights
        Console.WriteLine("Downloading OmniParser V2 models...");
        var pythonBin = Path.Combine(venvPath, "bin", "python");
        var weightsPath = Path.Combine(omniparserPath, "weights");

        try
        {
            // Try using huggingface-cli to download models
            var pipBin = Path.Combine(venvPath, "bin", "pip");

            // First, install huggingface-hub if not present
            await RunShellCommandAsync("\"" + pipBin + "\" install huggingface-hub", omniparserPath, ct);

            // Download models using huggingface-cli
            var huggingfaceCliBin = Path.Combine(venvPath, "bin", "huggingface-cli");
            await RunShellCommandAsync(
                "\"" + huggingfaceCliBin + "\" download microsoft/OmniParser-v2.0 --local-dir \"" + weightsPath + "\"",
                omniparserPath,
                ct);
        }
        catch
        {
            Console.WriteLine("⚠ Model download failed. You can download models manually:");
            Console.WriteLine("  pip install huggingface-hub");
            Console.WriteLine("  huggingface-cli download microsoft/OmniParser-v2.0 --local-dir ~/.nbot/omniparser/weights");
            Console.WriteLine();
            Console.WriteLine("Or download from HuggingFace web UI:");
            Console.WriteLine("  https://huggingface.co/microsoft/OmniParser-v2.0");
        }

        // Configuration complete
        config.Rpa = new RpaToolsConfig
        {
            Enabled = true,
            InstallPath = omniparserPath,
            ServicePort = 18999,
            AutoStartService = true
        };
        await ConfigurationLoader.SaveAsync(configPath, config, ct);
        Console.WriteLine("✓ OmniParser installed successfully");
        return true;
    }

    private static async Task<string?> GetPythonVersionAsync(CancellationToken ct)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "python3",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return output.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static async Task InstallPythonViaHomebrewAsync(CancellationToken ct)
    {
        Console.WriteLine("Installing Python via Homebrew...");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "brew",
                Arguments = "install python@3.11",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return;
            await process.WaitForExitAsync(ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Failed to install Python: {ex.Message}");
        }
    }

    private static string GetConfigDir(string configPath)
    {
        return Path.GetDirectoryName(configPath) ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nbot");
    }

    private static async Task RunShellCommandAsync(string command, string workingDir, CancellationToken ct)
    {
        var parts = command.Split(' ', 2);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = parts[0],
            Arguments = parts.Length > 1 ? parts[1] : "",
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null) return;
        await process.WaitForExitAsync(ct);
    }

    private static string GetEmbeddedRequirements() =>
        "flask>=3.0.0\nwerkzeug>=3.0.0\npillow>=10.0.0\nhuggingface-hub>=0.19.0\n";

    private static string GetEmbeddedServerScript() =>
        @"#!/usr/bin/env python3
import argparse
import base64
import logging
import time
from flask import Flask, jsonify, request

app = Flask(__name__)
parser = None

@app.route('/health')
def health():
    return jsonify({'status': 'ok'})

@app.route('/parse', methods=['POST'])
def parse():
    return jsonify({'error': 'OmniParser not configured'})

if __name__ == '__main__':
    app.run(host='127.0.0.1', port=18999)
";

    private static void PrintNextSteps(AgentConfig config, string configPath)
    {
        Console.WriteLine("\nNext steps:");
        Console.WriteLine("  • Chat: nbot agent");
        Console.WriteLine("  • Single message: nbot agent -m \"Hello!\"");
        Console.WriteLine("  • View config: nbot config --list");

        // 如果没有配置 LLM profile，提示用户配置
        if (string.IsNullOrEmpty(config.Llm.DefaultProfile) || config.Llm.Profiles.Count == 0)
        {
            Console.WriteLine("\nNote: No LLM profile configured. Run 'nbot onboard' to configure your LLM provider.");
            return;
        }

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

    /// <summary>
    /// Create default agent config
    /// </summary>
    private static AgentConfig CreateDefaultAgentConfig(string name, string workspacePath, EnvironmentInfo envInfo)
    {
        var config = new AgentConfig
        {
            Name = name,
            Workspace = { Path = workspacePath }
        };

        // 配置WebUI默认设置
        config.WebUI.Enabled = true;
        config.WebUI.Server.Host = "127.0.0.1";
        config.WebUI.Server.Port = 18888;
        config.WebUI.Auth.Mode = "token";
        config.WebUI.Auth.Token = GenerateRandomToken() ?? string.Empty;
        config.WebUI.Auth.AllowLocalhost = true;
        config.WebUI.Cors.AllowedOrigins.Add("http://localhost:18888");
        config.WebUI.Security.EnableHttps = false;
        config.WebUI.Features.FileUpload = true;
        config.WebUI.Features.MaxFileSize = "10MB";

        // 不创建空的 default LLM profile，让用户在 onboard 交互中配置
        config.Llm.Profiles.Clear();
        config.Llm.DefaultProfile = null;

        // 根据环境设置工具
        if (!envInfo.HasGui)
        {
            // 在无 GUI 环境下，禁用需要 GUI 的工具
            config.Browser = new BrowserToolsConfig { Enabled = false };
            // Note: Rap tool would be disabled here if it existed
        }

        return config;
    }

    /// <summary>
    /// Initialize workspace using WorkspaceManager to extract embedded resources
    /// </summary>
    private async Task InitializeWorkspaceAsync(string configPath, string workspacePath, CancellationToken cancellationToken)
    {
        Console.WriteLine("\nInitializing workspace...");

        // Create service collection for workspace initialization
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        services.AddSingleton(configPath);

        // Register workspace infrastructure
        services.AddSingleton<IEmbeddedResourceLoader, EmbeddedResourceLoader>();
        services.AddSingleton(configPath);

        var serviceProvider = services.BuildServiceProvider();

        // Create workspace config from resolved path
        var workspaceConfig = new WorkspaceConfig { Path = workspacePath };

        // Create and initialize WorkspaceManager
        var resourceLoader = serviceProvider.GetRequiredService<IEmbeddedResourceLoader>();
        var workspaceManager = new WorkspaceManager(workspaceConfig, resourceLoader);

        // Initialize workspace - this extracts all embedded resources (templates + skills)
        await workspaceManager.InitializeAsync(cancellationToken);

        Console.WriteLine($"✓ Workspace initialized at {workspacePath}");

        // List extracted files
        Console.WriteLine("\nExtracted workspace files:");
        var workspaceFiles = Directory.GetFiles(workspacePath, "*.md", SearchOption.TopDirectoryOnly);
        foreach (var file in workspaceFiles)
        {
            Console.WriteLine($"  • {Path.GetFileName(file)}");
        }

        var memoryFiles = Directory.GetFiles(Path.Combine(workspacePath, "memory"), "*.md", SearchOption.TopDirectoryOnly);
        foreach (var file in memoryFiles)
        {
            Console.WriteLine($"  • memory/{Path.GetFileName(file)}");
        }

        var sessionsDir = Path.Combine(workspacePath, "sessions");
        if (Directory.Exists(sessionsDir))
        {
            Console.WriteLine($"  • sessions/");
        }

        var skillsDir = Path.Combine(workspacePath, "skills");
        if (Directory.Exists(skillsDir))
        {
            var skillDirs = Directory.GetDirectories(skillsDir);
            if (skillDirs.Length > 0)
            {
                Console.WriteLine($"  • skills/ ({skillDirs.Length} skills)");
            }
            else
            {
                Console.WriteLine($"  • skills/");
            }
        }
    }

    /// <summary>
    /// Create workspace templates (legacy - use InitializeWorkspaceAsync instead)
    /// </summary>
    [Obsolete("Use InitializeWorkspaceAsync instead")]
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

        var sessionsDir = Path.Combine(workspacePath, "sessions");
        if (!Directory.Exists(sessionsDir))
        {
            Directory.CreateDirectory(sessionsDir);
            Console.WriteLine("  Created sessions/");
        }

        var skillsDir = Path.Combine(workspacePath, "skills");
        if (!Directory.Exists(skillsDir))
        {
            Directory.CreateDirectory(skillsDir);
        }
    }

    /// <summary>
    /// Get default AGENTS.md content
    /// </summary>
    private static string GetDefaultAgentsContent() => @"# Agent Instructions

You are a helpful AI assistant. Be concise, accurate, and friendly.

## Guidelines

- Always explain what you're doing before taking actions
- Ask for clarification when the request is ambiguous
- Use tools to help accomplish tasks
- Remember important information in memory/MEMORY.md; past conversations are stored in sessions/
";

    /// <summary>
    /// Get default SOUL.md content
    /// </summary>
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

    /// <summary>
    /// Get default USER.md content
    /// </summary>
    private static string GetDefaultUserContent() => @"# User

Information about the user goes here.

## Preferences

- Communication style: (casual/formal)
- Timezone: (your timezone)
- Language: (your preferred language)
";

    /// <summary>
    /// Get default MEMORY.md content
    /// </summary>
    private static string GetDefaultMemoryContent() => @"# Long-term Memory

This file stores important information that should persist across sessions.

## User Information

(Important facts about the user)

## Preferences

(User preferences learned over time)

## Important Notes

(Things to remember)
";

    /// <summary>
    /// Generate random token
    /// </summary>
    private static string GenerateRandomToken()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "").Substring(0, 32);
    }

    /// <summary>
    /// Get config path
    /// </summary>
    private static string GetConfigPath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".nbot", "config.json");
    }

    /// <summary>
    /// Get default workspace path
    /// </summary>
    private static string GetDefaultWorkspacePath() => "~/.nbot/workspace";

    /// <summary>
    /// Resolve path
    /// </summary>
    private static string ResolvePath(string path)
    {
        if (path.StartsWith("~/"))
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDir, path[2..]);
        }
        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Mask string for display
    /// </summary>
    private static string MaskString(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 8)
        {
            return "***";
        }
        return $"{value[..4]}...{value[^4..]}";
    }

    /// <summary>
    /// Normalize input character (convert full-width to half-width)
    /// </summary>
    private static char NormalizeInputChar(char c)
    {
        // Convert full-width digits (U+FF10-U+FF19) to half-width (U+0030-U+0039)
        if (c >= '\uFF10' && c <= '\uFF19')
        {
            return (char)(c - '\uFF10' + '0');
        }
        // Convert full-width uppercase letters (U+FF21-U+FF3A) to half-width (U+0041-U+005A)
        if (c >= '\uFF21' && c <= '\uFF3A')
        {
            return (char)(c - '\uFF21' + 'A');
        }
        // Convert full-width lowercase letters (U+FF41-U+FF5A) to half-width (U+0061-U+007A)
        if (c >= '\uFF41' && c <= '\uFF5A')
        {
            return (char)(c - '\uFF41' + 'a');
        }
        return c;
    }

    /// <summary>
    /// Environment information
    /// </summary>
    private class EnvironmentInfo
    {
        public string OsPlatform { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public bool HasGui { get; set; } = false;
        public bool ConfigExists { get; set; } = false;
    }

    /// <summary>
    /// Menu item
    /// </summary>
    private class MenuItem
    {
        public string Name { get; }
        public Func<Task>? Action { get; }

        public MenuItem(string name, Func<Task>? action)
        {
            Name = name;
            Action = action;
        }
    }
}
