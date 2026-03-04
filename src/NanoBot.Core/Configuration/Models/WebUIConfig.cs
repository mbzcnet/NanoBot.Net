namespace NanoBot.Core.Configuration;

public class WebUIConfig
{
    public bool Enabled { get; set; } = true;

    public WebUIServerConfig Server { get; set; } = new();

    public WebUIAuthConfig Auth { get; set; } = new();

    public WebUICorsConfig Cors { get; set; } = new();

    public WebUISecurityConfig Security { get; set; } = new();

    public WebUIFeaturesConfig Features { get; set; } = new();

    public WebUILocalizationConfig Localization { get; set; } = new();
}

public class WebUIServerConfig
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 18888;

    public string? Urls { get; set; }

    public string GetResolvedUrls()
    {
        return Urls ?? $"http://{Host}:{Port}";
    }
}

public class WebUIAuthConfig
{
    public string Mode { get; set; } = "token";

    public string? Token { get; set; }

    public bool AllowLocalhost { get; set; } = true;

    public string? Password { get; set; }
}

public class WebUICorsConfig
{
    public List<string> AllowedOrigins { get; set; } = new();

    public bool AllowAnyOrigin { get; set; } = false;

    public bool AllowAnyMethod { get; set; } = true;

    public bool AllowAnyHeader { get; set; } = true;

    public bool AllowCredentials { get; set; } = true;
}

public class WebUISecurityConfig
{
    public bool EnableHttps { get; set; } = false;

    public List<string> TrustedProxies { get; set; } = new();

    public bool EnableRateLimit { get; set; } = true;

    public int MaxRequestsPerMinute { get; set; } = 100;
}

public class WebUIFeaturesConfig
{
    public bool FileUpload { get; set; } = true;

    public string MaxFileSize { get; set; } = "10MB";

    public List<string> AllowedFileTypes { get; set; } = new()
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif"
    };
}

public class WebUILocalizationConfig
{
    public string DefaultLanguage { get; set; } = "auto";

    public List<string> SupportedLanguages { get; set; } = new()
    {
        "zh-CN", "en-US"
    };
}
