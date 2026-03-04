using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;

namespace NanoBot.WebUI.Services;

/// <summary>
/// WebUI 配置服务实现
/// 配置保存到独立文件 webui.settings.json，避免修改 appsettings.json 需要重启
/// </summary>
public class WebUIConfigService : IWebUIConfigService
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<WebUIConfigService> _logger;
    private readonly string _configPath;
    private WebUIConfig? _cachedConfig;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public WebUIConfigService(
        IHostEnvironment environment,
        ILogger<WebUIConfigService> logger)
    {
        _environment = environment;
        _logger = logger;
        _configPath = GetConfigFilePath();
    }

    public WebUIConfig GetConfig()
    {
        if (_cachedConfig != null)
        {
            return _cachedConfig;
        }

        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _cachedConfig = JsonSerializer.Deserialize<WebUIConfig>(json, JsonOptions) ?? CreateDefaultConfig();
            }
            else
            {
                _cachedConfig = CreateDefaultConfig();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载 WebUI 配置失败");
            _cachedConfig = CreateDefaultConfig();
        }

        return _cachedConfig;
    }

    public async Task<bool> SaveConfigAsync(WebUIConfig config)
    {
        try
        {
            var configDir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(_configPath, json);

            _cachedConfig = config;
            _logger.LogInformation("WebUI 配置已保存到 {ConfigPath}", _configPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存 WebUI 配置失败");
            return false;
        }
    }

    public string GetConfigPath() => _configPath;

    private static WebUIConfig CreateDefaultConfig() => new();

    private string GetConfigFilePath()
    {
        // 配置文件保存在用户目录下的 .nbot 文件夹
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var nbotDir = Path.Combine(homeDir, ".nbot");

        if (!Directory.Exists(nbotDir))
        {
            Directory.CreateDirectory(nbotDir);
        }

        return Path.Combine(nbotDir, "webui.settings.json");
    }
}
