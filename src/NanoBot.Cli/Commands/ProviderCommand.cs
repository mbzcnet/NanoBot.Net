using System.CommandLine;
using NanoBot.Core.Configuration;

namespace NanoBot.Cli.Commands;

public class ProviderCommand : ICliCommand
{
    public string Name => "provider";
    public string Description => "Manage providers";

    public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var loginCommand = new Command("login", "Authenticate with an OAuth provider");
        var providerArg = new Argument<string>("provider", "OAuth provider (e.g. 'openai-codex', 'github-copilot')");
        loginCommand.Add(providerArg);
        loginCommand.SetHandler(async (provider) =>
        {
            await LoginAsync(provider, cancellationToken);
        }, providerArg);

        var command = new Command(Name, Description);
        command.AddCommand(loginCommand);

        return await command.InvokeAsync(args);
    }

    private static Task LoginAsync(string provider, CancellationToken cancellationToken)
    {
        var normalizedProvider = provider.Replace("-", "_").ToLowerInvariant();

        Console.WriteLine($"🐈 OAuth Login - {provider}\n");

        switch (normalizedProvider)
        {
            case "openai_codex":
                LoginOpenAICodex();
                break;

            case "github_copilot":
                LoginGitHubCopilot();
                break;

            default:
                Console.WriteLine($"Unknown OAuth provider: {provider}");
                Console.WriteLine("Supported providers: openai-codex, github-copilot");
                break;
        }

        return Task.CompletedTask;
    }

    private static void LoginOpenAICodex()
    {
        Console.WriteLine("OpenAI Codex OAuth login requires the oauth-cli-kit package.");
        Console.WriteLine("In .NET, you can configure OpenAI Codex by:");
        Console.WriteLine("  1. Running: dotnet user-secrets set \"OpenAI:ApiKey\" \"your-api-key\"");
        Console.WriteLine("  2. Or setting environment variable: OPENAI_API_KEY");
        Console.WriteLine("\nFor OAuth flow, please use the Python version or configure manually.");
    }

    private static void LoginGitHubCopilot()
    {
        Console.WriteLine("GitHub Copilot device flow...");
        Console.WriteLine("In .NET, you can configure GitHub Copilot by:");
        Console.WriteLine("  1. Getting a token from: https://github.com/settings/tokens");
        Console.WriteLine("  2. Setting: dotnet user-secrets set \"GitHub:Copilot:Token\" \"your-token\"");
        Console.WriteLine("  3. Or setting environment variable: GITHUB_COPILOT_TOKEN");
        Console.WriteLine("\nFor device flow, please use the Python version.");
    }
}
