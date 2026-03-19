using System.CommandLine;
using NanoBot.Core.Configuration;
using NanoBot.Cli.Services;
using NanoBot.Infrastructure.Browser;

namespace NanoBot.Cli.Commands;

public class OnboardCommand : ICliCommand
{
    public string Name => "onboard";
    public string Description => "Initialize nbot configuration and workspace (and optionally configure LLM)";

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
                config = CreateDefaultAgentConfig(name ?? "NanoBot", workspacePath);
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
            config = CreateDefaultAgentConfig(name ?? "NanoBot", workspacePath);
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

        // Configure LLM and workspace first, then offer browser tools at the end
        if (nonInteractive)
        {
            ApplyNonInteractiveOptions(config, provider, model, apiKey, apiBase, workspace);
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            PrintNextSteps(config, configPath);

            // Offer browser tools installation at the end (optional)
            if (!skipBrowserInstall)
            {
                Console.WriteLine();
                await InstallPlaywrightBrowsersAsync(config, configPath, nonInteractive, cancellationToken);
            }

            // Offer RPA tools (OmniParser) installation
            if (!skipOmniParser)
            {
                Console.WriteLine();
                await InstallOmniParserAsync(config, configPath, nonInteractive, cancellationToken);
            }
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

        // Browser tools setup is offered after LLM configuration is complete
        if (!skipBrowserInstall)
        {
            Console.WriteLine();
            await InstallPlaywrightBrowsersAsync(config, configPath, nonInteractive, cancellationToken);
        }
    }

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

    private static string GetConfigPath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".nbot", "config.json");
    }

    private static string GetDefaultWorkspacePath() => "~/.nbot/workspace";

    private static string ResolvePath(string path)
    {
        if (path.StartsWith("~/"))
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDir, path[2..]);
        }
        return Path.GetFullPath(path);
    }

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

        // Playwright not installed, ask user whether to install
        if (!nonInteractive)
        {
            Console.WriteLine("Browser automation tools require Playwright browsers to be installed.");
            Console.Write("Install Playwright browsers now? [Y/n]: ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response == "n" || response == "no")
            {
                Console.WriteLine("⚠ Skipped Playwright browser installation.");
                Console.WriteLine("  You can install manually later by running:");
                Console.WriteLine("    dotnet tool install --global Microsoft.Playwright.CLI");
                Console.WriteLine("    playwright install chromium");
                config.Browser = new BrowserToolsConfig { Enabled = false };
                await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                return false;
            }
        }
        else
        {
            // In non-interactive mode, skip installation silently
            config.Browser = new BrowserToolsConfig { Enabled = false };
            await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
            return false;
        }

        // User agreed to install, check PowerShell availability
        Console.WriteLine("Checking PowerShell availability...");
        var pwshPath = await powerShellInstaller.GetPowerShellPathAsync(cancellationToken);

        if (string.IsNullOrEmpty(pwshPath))
        {
            // PowerShell not found, ask user whether to install
            Console.WriteLine("PowerShell Core (pwsh) is required for Playwright installation.");
            Console.WriteLine("  You can install manually from: https://aka.ms/powershell");

            if (!nonInteractive)
            {
                Console.Write("\nInstall PowerShell Core now? [Y/n]: ");
                var response = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (response == "n" || response == "no")
                {
                    Console.WriteLine("⚠ Skipped PowerShell installation. Playwright installation aborted.");
                    config.Browser = new BrowserToolsConfig { Enabled = false };
                    await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                    return false;
                }

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
                // In non-interactive mode, skip silently
                config.Browser = new BrowserToolsConfig { Enabled = false };
                await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                return false;
            }
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

    private static string GetDefaultAgentsContent() => @"# Agent Instructions

You are a helpful AI assistant. Be concise, accurate, and friendly.

## Guidelines

- Always explain what you're doing before taking actions
- Ask for clarification when the request is ambiguous
- Use tools to help accomplish tasks
- Remember important information in memory/MEMORY.md; past conversations are stored in sessions/
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

    private static AgentConfig CreateDefaultAgentConfig(string name, string workspacePath)
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

        return config;
    }

    private static string GenerateRandomToken()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "").Substring(0, 32);
    }
}
