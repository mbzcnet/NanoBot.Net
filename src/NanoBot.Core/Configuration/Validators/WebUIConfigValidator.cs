using System.Net;
using NanoBot.Core.Configuration;

namespace NanoBot.Core.Configuration.Validators;

public static class WebUIConfigValidator
{
    public static ValidationResult Validate(WebUIConfig config)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (config == null)
        {
            errors.Add("WebUI配置不能为空");
            return new ValidationResult(errors, warnings);
        }

        ValidateServerConfig(config.Server, errors, warnings);
        ValidateAuthConfig(config.Auth, errors, warnings);
        ValidateCorsConfig(config.Cors, errors, warnings);
        ValidateSecurityConfig(config.Security, errors, warnings);
        ValidateFeaturesConfig(config.Features, errors, warnings);

        return new ValidationResult(errors, warnings);
    }

    private static void ValidateServerConfig(WebUIServerConfig server, List<string> errors, List<string> warnings)
    {
        if (server == null)
        {
            errors.Add("Server配置不能为空");
            return;
        }

        // 验证主机地址
        if (!string.IsNullOrEmpty(server.Host))
        {
            if (!IsValidHost(server.Host))
            {
                errors.Add($"无效的主机地址: {server.Host}");
            }
        }

        // 验证端口
        if (server.Port < 1 || server.Port > 65535)
        {
            errors.Add($"端口必须在1-65535范围内: {server.Port}");
        }

        // 验证URLs
        if (!string.IsNullOrEmpty(server.Urls))
        {
            if (!IsValidUrls(server.Urls))
            {
                errors.Add($"无效的URLs配置: {server.Urls}");
            }
        }
    }

    private static void ValidateAuthConfig(WebUIAuthConfig auth, List<string> errors, List<string> warnings)
    {
        if (auth == null)
        {
            errors.Add("Auth配置不能为空");
            return;
        }

        var validModes = new[] { "none", "token", "password" };
        if (!validModes.Contains(auth.Mode.ToLowerInvariant()))
        {
            errors.Add($"无效的认证模式: {auth.Mode}。支持的模式: {string.Join(", ", validModes)}");
        }

        if (auth.Mode.Equals("token", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(auth.Token))
        {
            warnings.Add("Token认证模式下建议设置Token");
        }

        if (auth.Mode.Equals("password", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(auth.Password))
        {
            errors.Add("Password认证模式下必须设置Password");
        }
    }

    private static void ValidateCorsConfig(WebUICorsConfig cors, List<string> errors, List<string> warnings)
    {
        if (cors == null)
        {
            errors.Add("Cors配置不能为空");
            return;
        }

        if (!cors.AllowAnyOrigin && cors.AllowedOrigins.Count == 0)
        {
            warnings.Add("CORS配置中未设置允许的源，可能影响跨域访问");
        }

        foreach (var origin in cors.AllowedOrigins)
        {
            if (!IsValidOrigin(origin))
            {
                errors.Add($"无效的CORS源: {origin}");
            }
        }
    }

    private static void ValidateSecurityConfig(WebUISecurityConfig security, List<string> errors, List<string> warnings)
    {
        if (security == null)
        {
            errors.Add("Security配置不能为空");
            return;
        }

        if (security.MaxRequestsPerMinute < 1)
        {
            errors.Add($"最大请求数必须大于0: {security.MaxRequestsPerMinute}");
        }

        foreach (var proxy in security.TrustedProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                errors.Add($"无效的代理IP地址: {proxy}");
            }
        }
    }

    private static void ValidateFeaturesConfig(WebUIFeaturesConfig features, List<string> errors, List<string> warnings)
    {
        if (features == null)
        {
            errors.Add("Features配置不能为空");
            return;
        }

        if (!IsValidFileSize(features.MaxFileSize))
        {
            errors.Add($"无效的文件大小格式: {features.MaxFileSize}。支持的格式: 10MB, 1GB等");
        }

        foreach (var fileType in features.AllowedFileTypes)
        {
            if (!fileType.StartsWith(".") || fileType.Length < 2)
            {
                errors.Add($"无效的文件类型: {fileType}。必须以点开头，如.png");
            }
        }
    }

    private static bool IsValidHost(string host)
    {
        return IPAddress.TryParse(host, out _) ||
               host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
               (host.Contains('.') && Uri.CheckHostName(host) == UriHostNameType.Dns);
    }

    private static bool IsValidUrls(string urls)
    {
        try
        {
            var urlList = urls.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var url in urlList)
            {
                if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
                {
                    return false;
                }

                if (uri.Scheme != "http" && uri.Scheme != "https")
                {
                    return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidOrigin(string origin)
    {
        if (origin.Equals("*", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
               (uri.Scheme == "http" || uri.Scheme == "https");
    }

    private static bool IsValidFileSize(string size)
    {
        if (string.IsNullOrEmpty(size))
            return false;

        var pattern = @"^\d+(\.\d+)?\s*(KB|MB|GB|TB)$";
        return System.Text.RegularExpressions.Regex.IsMatch(size.Trim(), pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
