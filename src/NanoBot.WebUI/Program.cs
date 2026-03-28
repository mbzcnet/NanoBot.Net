using MudBlazor.Services;
using NanoBot.WebUI.Components;
using NanoBot.WebUI.Services;
using NanoBot.WebUI.Middleware;
using NanoBot.Agent;
using NanoBot.Core.Configuration;
using NanoBot.Core.Configuration.Validators;
using NanoBot.Core.Sessions;
using System.Globalization;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

var configPath = GetConfigPath(args);
if (string.IsNullOrEmpty(configPath))
{
    configPath = ConfigurationChecker.ResolveExistingConfigPath();
}

AgentConfig? agentConfig = null;

if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
{
    try
    {
        agentConfig = await ConfigurationLoader.LoadWithDefaultsAsync(configPath, CancellationToken.None);
        LogResolvedConfiguration("WebUI", configPath, agentConfig, usingDefaultConfig: false);
        
        var validationResult = WebUIConfigValidator.Validate(agentConfig.WebUI);
        if (!validationResult.IsValid)
        {
            Console.WriteLine("❌ WebUI配置验证失败:");
            Console.WriteLine(validationResult.GetSummary());
            Environment.Exit(1);
        }
        
        if (validationResult.Warnings.Count > 0)
        {
            Console.WriteLine("⚠️  WebUI配置警告:");
            Console.WriteLine(validationResult.GetSummary());
        }
        
        Console.WriteLine($"✓ 配置已从 {configPath} 加载");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ 配置加载失败: {ex.Message}");
        Environment.Exit(1);
    }
}
else
{
    Console.WriteLine("⚠️  未找到配置文件，使用默认配置");
    agentConfig = new AgentConfig();
    LogResolvedConfiguration("WebUI", configPath, agentConfig, usingDefaultConfig: true);
}

ApplyWebUIConfiguration(builder.Configuration, agentConfig.WebUI, args);

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddHttpContextAccessor();

builder.Services.AddMudServices();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddControllers();
builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("WebUI:Cors:AllowedOrigins").Get<string[]>() 
            ?? new[] { "http://localhost:18888" };
        
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddNanoBot(agentConfig);

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<SessionMessageParser>();
builder.Services.AddSingleton<ISessionService, SessionService>();
builder.Services.AddSingleton<IAgentService, AgentService>();
builder.Services.AddScoped<ILocalizationService, LocalizationService>();
builder.Services.AddSingleton<IWebUIConfigService, WebUIConfigService>();

var app = builder.Build();

// 使用用户友好的异常处理中间件（在开发和生产环境都启用）
app.UseUserFriendlyExceptions();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

var configuredUrls = builder.Configuration["WebUI:Server:Urls"]
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
var shouldUseHttpsRedirect = string.IsNullOrEmpty(configuredUrls)
    || configuredUrls.Contains("https", StringComparison.OrdinalIgnoreCase);

if (shouldUseHttpsRedirect)
{
    app.UseHttpsRedirection();
}
else
{
    app.Logger.LogWarning("HTTPS redirection disabled because configured URLs are HTTP-only: {Urls}", configuredUrls);
}

var supportedCultures = new[] { "zh-CN", "en-US" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

// 使用 Cookie 和查询字符串文化特性选择器
localizationOptions.RequestCultureProviders.Insert(0, new CookieRequestCultureProvider()
{
    CookieName = ".AspNetCore.Culture"
});

app.UseRequestLocalization(localizationOptions);

app.UseStaticFiles();
app.UseAntiforgery();
app.UseCors();
app.MapHub<NanoBot.WebUI.Hubs.ChatHub>("/hub/chat");
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string GetConfigPath(string[] args)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals("--config", StringComparison.OrdinalIgnoreCase) || 
            args[i].Equals("-c", StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }
    return string.Empty;
}

static void LogResolvedConfiguration(string source, string? configPath, AgentConfig config, bool usingDefaultConfig)
{
    var defaultProfileId = config.Llm.DefaultProfile ?? "default";
    config.Llm.Profiles.TryGetValue(defaultProfileId, out var profile);

    Console.WriteLine($"[NanoBot Config] source={source} configPath={(string.IsNullOrWhiteSpace(configPath) ? "<default>" : configPath)} usingDefaultConfig={usingDefaultConfig}");
    Console.WriteLine($"[NanoBot Config] workspace={config.Workspace.Path} defaultProfile={defaultProfileId} provider={profile?.Provider ?? "openai"} model={profile?.Model ?? "<unknown>"} apiBase={profile?.ApiBase ?? "<null>"} maxTokens={profile?.MaxTokens}");
}

static void ApplyWebUIConfiguration(IConfiguration configuration, WebUIConfig webUIConfig, string[] args)
{
    var urlsFromArgs = GetUrlsFromArgs(args);
    if (!string.IsNullOrEmpty(urlsFromArgs))
    {
        return;
    }
    
    if (!string.IsNullOrEmpty(webUIConfig.Server.GetResolvedUrls()))
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", webUIConfig.Server.GetResolvedUrls());
    }
}

static string GetUrlsFromArgs(string[] args)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals("--urls", StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }
    return string.Empty;
}
