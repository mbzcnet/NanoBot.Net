using NanoBot.Core.Configuration;

namespace NanoBot.Cli.Services;

/// <summary>
/// 提供交互式 LLM Profile 配置服务
/// </summary>
public class LlmProfileConfigService
{
    /// <summary>
    /// 交互式配置单个 LLM Profile
    /// </summary>
    public async Task<bool> ConfigureProfileInteractiveAsync(
        AgentConfig config,
        string? profileName = null,
        CancellationToken cancellationToken = default)
    {
        profileName ??= config.Llm.DefaultProfile ?? "default";
        
        Console.WriteLine($"=== Configuring LLM Profile: {profileName} ===\n");

        if (!config.Llm.Profiles.ContainsKey(profileName))
        {
            config.Llm.Profiles[profileName] = new LlmProfile { Name = profileName };
        }
        var profile = config.Llm.Profiles[profileName];

        var provider = await PromptProviderAsync(profile.Provider, cancellationToken);
        if (provider == null)
        {
            Console.WriteLine("Cancelled.");
            return false;
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
            var apiKey = await PromptApiKeyAsync(provider, cancellationToken);
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

        Console.WriteLine($"\n✓ LLM Profile '{profileName}' configured:");
        Console.WriteLine($"  Provider: {profile.Provider}");
        Console.WriteLine($"  Model: {profile.Model}");
        Console.WriteLine($"  API Key: {(string.IsNullOrEmpty(profile.ApiKey) ? "(using environment variable)" : MaskApiKey(profile.ApiKey))}");
        Console.WriteLine($"  API URL: {MaskApiUrl(profile.ApiBase)}");

        return true;
    }

    /// <summary>
    /// 交互式管理多个 LLM Profiles
    /// </summary>
    public async Task ManageProfilesInteractiveAsync(
        AgentConfig config,
        string configPath,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Console.WriteLine("\n=== LLM Profile Management ===\n");
            Console.WriteLine($"Default Profile: {config.Llm.DefaultProfile ?? "default"}\n");

            var profiles = config.Llm.Profiles.Keys.ToList();
            if (profiles.Count == 0)
            {
                Console.WriteLine("No profiles configured.\n");
            }
            else
            {
                Console.WriteLine("Existing Profiles:");
                for (var i = 0; i < profiles.Count; i++)
                {
                    var profileName = profiles[i];
                    var profile = config.Llm.Profiles[profileName];
                    var isDefault = profileName == (config.Llm.DefaultProfile ?? "default");
                    var marker = isDefault ? "*" : " ";
                    Console.WriteLine($"  {marker} [{i + 1}] {profileName} ({profile.Provider}/{profile.Model})");
                }
                Console.WriteLine();
            }

            Console.WriteLine("Actions:");
            Console.WriteLine("  [A] Add new profile");
            Console.WriteLine("  [E] Edit existing profile");
            Console.WriteLine("  [D] Delete profile");
            Console.WriteLine("  [S] Set default profile");
            Console.WriteLine("  [Q] Save and quit");
            Console.Write("\nSelect action: ");

            var key = Console.ReadKey(true);
            var action = char.ToUpperInvariant(key.KeyChar);
            Console.WriteLine(action);

            switch (action)
            {
                case 'A':
                    await AddProfileAsync(config, cancellationToken);
                    break;
                case 'E':
                    await EditProfileAsync(config, cancellationToken);
                    break;
                case 'D':
                    DeleteProfile(config);
                    break;
                case 'S':
                    SetDefaultProfile(config);
                    break;
                case 'Q':
                    await ConfigurationLoader.SaveAsync(configPath, config, cancellationToken);
                    Console.WriteLine($"\n✓ Configuration saved to {configPath}");
                    return;
                default:
                    Console.WriteLine("Invalid action.");
                    break;
            }
        }
    }

    private async Task AddProfileAsync(AgentConfig config, CancellationToken cancellationToken)
    {
        Console.Write("\nEnter new profile name: ");
        var profileName = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(profileName))
        {
            Console.WriteLine("Invalid profile name.");
            return;
        }

        if (config.Llm.Profiles.ContainsKey(profileName))
        {
            Console.WriteLine($"Profile '{profileName}' already exists.");
            return;
        }

        await ConfigureProfileInteractiveAsync(config, profileName, cancellationToken);
    }

    private async Task EditProfileAsync(AgentConfig config, CancellationToken cancellationToken)
    {
        if (config.Llm.Profiles.Count == 0)
        {
            Console.WriteLine("\nNo profiles to edit.");
            return;
        }

        var profiles = config.Llm.Profiles.Keys.ToList();
        Console.WriteLine("\nSelect profile to edit:");
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

        await ConfigureProfileInteractiveAsync(config, selectedProfile, cancellationToken);
    }

    private void DeleteProfile(AgentConfig config)
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

    private async Task<string?> PromptProviderAsync(string? currentProvider, CancellationToken cancellationToken)
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
                Console.SetCursorPosition(0, Console.CursorTop - providers.Count);
            }
            else if (key.Key == ConsoleKey.DownArrow && currentIndex < providers.Count - 1)
            {
                currentIndex++;
                Console.SetCursorPosition(0, Console.CursorTop - providers.Count);
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

    private async Task<string?> PromptApiKeyAsync(string provider, CancellationToken cancellationToken)
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
}
