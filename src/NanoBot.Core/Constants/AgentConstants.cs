namespace NanoBot.Core.Constants;

public static class Bootstrap
{
    public const string AgentsFile = "AGENTS.md";
    public const string SoulFile = "SOUL.md";
    public const string UserFile = "USER.md";
    public const string ToolsFile = "TOOLS.md";
    public const string IdentityFile = "IDENTITY.md";

    public static readonly string[] AllFiles = [AgentsFile, SoulFile, UserFile, ToolsFile];
}

public static class Commands
{
    public const string New = "/new";
    public const string Help = "/help";
    public const string Stop = "/stop";
    public const string Clear = "/clear";
    public const string Exit = "/exit";
}

public static class EnvironmentVariables
{
    public const string OpenAiApiKey = "OPENAI_API_KEY";
    public const string AnthropicApiKey = "ANTHROPIC_API_KEY";
    public const string OpenRouterApiKey = "OPENROUTER_API_KEY";
    public const string AzureOpenAiApiKey = "AZURE_OPENAI_API_KEY";
    public const string GoogleApiKey = "GOOGLE_API_KEY";
    public const string OllamaBaseUrl = "OLLAMA_BASE_URL";
}

public static class Channels
{
    public const string Telegram = "telegram";
    public const string Discord = "discord";
    public const string Slack = "slack";
    public const string WhatsApp = "whatsapp";
    public const string WebUI = "webui";
}
