using System.Net;

namespace NanoBot.Core.Configuration.Validators;

public static class WebUIConfigValidator
{
    public static ValidationResult Validate(WebUIConfig config)
    {
        var result = new ValidationResult();

        if (config == null)
        {
            result.AddError("WebUI配置不能为空");
            return result;
        }

        ValidateServerConfig(config.Server, result);
        ValidateAuthConfig(config.Auth, result);
        ValidateCorsConfig(config.Cors, result);
        ValidateSecurityConfig(config.Security, result);
        ValidateFeaturesConfig(config.Features, result);

        return result;
    }

    private static void ValidateServerConfig(WebUIServerConfig server, ValidationResult result)
    {
        if (server == null)
        {
            result.AddError("Server配置不能为空");
            return;
        }

        // 验证主机地址
        if (!string.IsNullOrEmpty(server.Host))
        {
            if (!IsValidHost(server.Host))
            {
                result.AddError($"无效的主机地址: {server.Host}");
            }
        }

        // 验证端口
        if (server.Port < 1 || server.Port > 65535)
        {
            result.AddError($"端口必须在1-65535范围内: {server.Port}");
        }

        // 验证URLs
        if (!string.IsNullOrEmpty(server.Urls))
        {
            if (!IsValidUrls(server.Urls))
            {
                result.AddError($"无效的URLs配置: {server.Urls}");
            }
        }
    }

    private static void ValidateAuthConfig(WebUIAuthConfig auth, ValidationResult result)
    {
        if (auth == null)
        {
            result.AddError("Auth配置不能为空");
            return;
        }

        var validModes = new[] { "none", "token", "password" };
        if (!validModes.Contains(auth.Mode.ToLowerInvariant()))
        {
            result.AddError($"无效的认证模式: {auth.Mode}。支持的模式: {string.Join(", ", validModes)}");
        }

        if (auth.Mode.Equals("token", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(auth.Token))
        {
            result.AddWarning("Token认证模式下建议设置Token");
        }

        if (auth.Mode.Equals("password", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(auth.Password))
        {
            result.AddError("Password认证模式下必须设置Password");
        }
    }

    private static void ValidateCorsConfig(WebUICorsConfig cors, ValidationResult result)
    {
        if (cors == null)
        {
            result.AddError("Cors配置不能为空");
            return;
        }

        if (!cors.AllowAnyOrigin && cors.AllowedOrigins.Count == 0)
        {
            result.AddWarning("CORS配置中未设置允许的源，可能影响跨域访问");
        }

        foreach (var origin in cors.AllowedOrigins)
        {
            if (!IsValidOrigin(origin))
            {
                result.AddError($"无效的CORS源: {origin}");
            }
        }
    }

    private static void ValidateSecurityConfig(WebUISecurityConfig security, ValidationResult result)
    {
        if (security == null)
        {
            result.AddError("Security配置不能为空");
            return;
        }

        if (security.MaxRequestsPerMinute < 1)
        {
            result.AddError($"最大请求数必须大于0: {security.MaxRequestsPerMinute}");
        }

        foreach (var proxy in security.TrustedProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                result.AddError($"无效的代理IP地址: {proxy}");
            }
        }
    }

    private static void ValidateFeaturesConfig(WebUIFeaturesConfig features, ValidationResult result)
    {
        if (features == null)
        {
            result.AddError("Features配置不能为空");
            return;
        }

        if (!IsValidFileSize(features.MaxFileSize))
        {
            result.AddError($"无效的文件大小格式: {features.MaxFileSize}。支持的格式: 10MB, 1GB等");
        }

        foreach (var fileType in features.AllowedFileTypes)
        {
            if (!fileType.StartsWith(".") || fileType.Length < 2)
            {
                result.AddError($"无效的文件类型: {fileType}。必须以点开头，如.png");
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

public class ValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public bool IsValid => Errors.Count == 0;

    public void AddError(string error)
    {
        Errors.Add(error);
    }

    public void AddWarning(string warning)
    {
        Warnings.Add(warning);
    }

    public string GetSummary()
    {
        var summary = new List<string>();
        
        if (Errors.Count > 0)
        {
            summary.Add($"错误 ({Errors.Count}):\n- {string.Join("\n- ", Errors)}");
        }
        
        if (Warnings.Count > 0)
        {
            summary.Add($"警告 ({Warnings.Count}):\n- {string.Join("\n- ", Warnings)}");
        }

        return string.Join("\n\n", summary);
    }
}
