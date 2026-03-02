using MudBlazor.Services;
using NanoBot.WebUI.Components;
using NanoBot.WebUI.Services;
using NanoBot.Cli.Extensions;
using NanoBot.Cli.Services;
using NanoBot.Core.Configuration;
using NanoBot.Core.Configuration.Validators;

var builder = WebApplication.CreateBuilder(args);

// 解析配置文件参数
var configPath = GetConfigPath(args);
AgentConfig? agentConfig = null;

// 加载主配置文件
if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
{
    try
    {
        agentConfig = await ConfigurationLoader.LoadAsync(configPath, CancellationToken.None);
        
        // 验证WebUI配置
        var validationResult = WebUIConfigValidator.Validate(agentConfig.WebUI);
        if (!validationResult.IsValid)
        {
            Console.WriteLine("❌ WebUI配置验证失败:");
            Console.WriteLine(validationResult.GetSummary());
            Environment.Exit(1);
        }
        
        // 显示警告
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
}

// 应用WebUI配置到ASP.NET Core配置
ApplyWebUIConfiguration(builder.Configuration, agentConfig.WebUI, args);

// Add MudBlazor services
builder.Services.AddMudServices();

// Add Razor Components with Server interactivity
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add Controllers
builder.Services.AddControllers();

// Add SignalR
builder.Services.AddSignalR();

// Add CORS
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

// Load NanoBot configuration
var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var defaultConfigPath = Path.Combine(homeDir, ".nbot", "config.json");

if (File.Exists(defaultConfigPath) && agentConfig == null)
{
    agentConfig = await ConfigurationLoader.LoadAsync(defaultConfigPath, CancellationToken.None);
    builder.Services.AddNanoBotConfiguration(builder.Configuration);
    
    // 手动注册配置对象
    builder.Services.AddSingleton(agentConfig);
    builder.Services.AddSingleton(agentConfig.Workspace);
    builder.Services.AddSingleton(agentConfig.Llm);
    
    // Add NanoBot services
    builder.Services.AddMicrosoftAgentsAI(agentConfig.Llm);
    builder.Services.AddNanoBotTools();
    builder.Services.AddNanoBotContextProviders();
    builder.Services.AddNanoBotInfrastructure(agentConfig.Workspace);
    builder.Services.AddNanoBotBackgroundServices();
    builder.Services.AddNanoBotAgent();
}
else if (agentConfig != null)
{
    builder.Services.AddNanoBotConfiguration(builder.Configuration);
    
    // 手动注册配置对象
    builder.Services.AddSingleton(agentConfig);
    builder.Services.AddSingleton(agentConfig.Workspace);
    builder.Services.AddSingleton(agentConfig.Llm);
    
    // Add NanoBot services
    builder.Services.AddMicrosoftAgentsAI(agentConfig.Llm);
    builder.Services.AddNanoBotTools();
    builder.Services.AddNanoBotContextProviders();
    builder.Services.AddNanoBotInfrastructure(agentConfig.Workspace);
    builder.Services.AddNanoBotBackgroundServices();
    builder.Services.AddNanoBotAgent();
}
else
{
    // 如果配置不存在，使用默认配置
    var defaultConfig = new AgentConfig();
    builder.Services.AddSingleton(defaultConfig);
    builder.Services.AddSingleton(defaultConfig.Workspace);
    builder.Services.AddSingleton(defaultConfig.Llm);
}

// Add WebUI services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<ISessionService, SessionService>();
builder.Services.AddSingleton<IAgentService, AgentService>();
builder.Services.AddSingleton<NanoBot.Core.Storage.IFileStorageService, NanoBot.Infrastructure.Storage.FileStorageService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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

app.UseStaticFiles();
app.UseAntiforgery();

// Use CORS
app.UseCors();

// Map SignalR Hub
app.MapHub<NanoBot.WebUI.Hubs.ChatHub>("/hub/chat");

// Map Controllers
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

static void ApplyWebUIConfiguration(IConfiguration configuration, WebUIConfig webUIConfig, string[] args)
{
    // 检查命令行参数是否指定了URLs
    var urlsFromArgs = GetUrlsFromArgs(args);
    if (!string.IsNullOrEmpty(urlsFromArgs))
    {
        // 命令行参数优先级最高
        return;
    }
    
    // 应用WebUI配置中的URLs
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
