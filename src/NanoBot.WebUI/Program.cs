using MudBlazor.Services;
using NanoBot.WebUI.Components;
using NanoBot.WebUI.Services;
using NanoBot.Cli.Extensions;
using NanoBot.Cli.Services;
using NanoBot.Core.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

// Add Razor Components with Server interactivity
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add SignalR
builder.Services.AddSignalR();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("WebUI:Cors:AllowedOrigins").Get<string[]>() 
            ?? new[] { "http://localhost:5000" };
        
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Load NanoBot configuration
var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var configPath = Path.Combine(homeDir, ".nbot", "config.json");
AgentConfig? agentConfig = null;

if (File.Exists(configPath))
{
    agentConfig = await ConfigurationLoader.LoadAsync(configPath, CancellationToken.None);
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

// Use CORS
app.UseCors();

// Map SignalR Hub
app.MapHub<NanoBot.WebUI.Hubs.ChatHub>("/hub/chat");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
