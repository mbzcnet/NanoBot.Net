using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace NanoBot.WebUI.Middleware;

public class UserFriendlyExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserFriendlyExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public UserFriendlyExceptionMiddleware(
        RequestDelegate next,
        ILogger<UserFriendlyExceptionMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var errorInfo = GetErrorInfo(exception);

        // 检查是否是 API 请求
        if (IsApiRequest(context))
        {
            context.Response.ContentType = "application/json";
            var problemDetails = new ProblemDetails
            {
                Status = context.Response.StatusCode,
                Title = errorInfo.Title,
                Detail = errorInfo.Message,
                Instance = context.Request.Path
            };
            await JsonSerializer.SerializeAsync(context.Response.Body, problemDetails);
        }
        else
        {
            // 渲染友好的 HTML 错误页面
            context.Response.ContentType = "text/html; charset=utf-8";
            var html = BuildErrorPage(errorInfo, exception);
            await context.Response.WriteAsync(html);
        }
    }

    private static bool IsApiRequest(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        return path.StartsWith("/api/") || 
               path.StartsWith("/hub/") ||
               context.Request.Headers.Accept.Any(h => h?.Contains("application/json") == true);
    }

    private ErrorInfo GetErrorInfo(Exception exception)
    {
        var message = exception.Message;

        // API Key 缺失错误
        if (message.Contains("Missing API key for provider", StringComparison.OrdinalIgnoreCase))
        {
            var provider = ExtractProviderName(message);
            return new ErrorInfo(
                "配置错误：缺少 API Key",
                $"""
                您使用的 AI 提供商 '{provider}' 需要 API Key 才能正常工作。
                
                解决方法：
                1. 在配置文件中设置：llm.profiles.default.api_key
                2. 或通过环境变量设置：{GetEnvVarName(provider)}
                """,
                ErrorType.Configuration,
                exception
            );
        }

        // LLM Profile 未找到
        if (message.Contains("not found in configuration", StringComparison.OrdinalIgnoreCase))
        {
            return new ErrorInfo(
                "配置错误：LLM 配置不完整",
                """
                找不到指定的 LLM 配置文件。
                
                解决方法：
                1. 检查配置文件中的 llm.profiles 部分
                2. 确保 default 配置存在且正确
                """,
                ErrorType.Configuration,
                exception
            );
        }

        // 不支持的提供商
        if (message.Contains("is not supported", StringComparison.OrdinalIgnoreCase))
        {
            return new ErrorInfo(
                "配置错误：不支持的 AI 提供商",
                $"""
                {message}
                
                支持的提供商：OpenAI、OpenRouter、Anthropic、DeepSeek、Groq、Moonshot、Zhipu AI、Ollama、VolcEngine、SiliconFlow
                """,
                ErrorType.Configuration,
                exception
            );
        }

        // 配置文件错误
        if (exception is FileNotFoundException || message.Contains("config", StringComparison.OrdinalIgnoreCase))
        {
            return new ErrorInfo(
                "配置错误",
                """
                无法加载配置文件。
                
                解决方法：
                1. 确保配置文件路径正确
                2. 检查配置文件格式是否为有效的 YAML
                3. 使用 --config 参数指定配置文件路径
                """,
                ErrorType.Configuration,
                exception
            );
        }

        // 网络/连接错误
        if (exception is HttpRequestException || message.Contains("connection", StringComparison.OrdinalIgnoreCase))
        {
            return new ErrorInfo(
                "网络错误",
                """
                无法连接到 AI 服务。
                
                可能原因：
                1. 网络连接问题
                2. API 服务暂时不可用
                3. 代理设置问题
                
                请检查网络连接后重试。
                """,
                ErrorType.Network,
                exception
            );
        }

        // 默认错误
        return new ErrorInfo(
            "应用程序错误",
            _environment.IsDevelopment() ? message : "应用程序遇到了意外错误，请查看日志获取详细信息。",
            ErrorType.Unknown,
            exception
        );
    }

    private static string ExtractProviderName(string message)
    {
        var start = message.IndexOf("'", StringComparison.Ordinal);
        var end = message.IndexOf("'", start + 1);
        if (start > 0 && end > start)
        {
            return message.Substring(start + 1, end - start - 1);
        }
        return "unknown";
    }

    private static string GetEnvVarName(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "openai" => "OPENAI_API_KEY",
            "openrouter" => "OPENROUTER_API_KEY",
            "anthropic" => "ANTHROPIC_API_KEY",
            "deepseek" => "DEEPSEEK_API_KEY",
            "groq" => "GROQ_API_KEY",
            "moonshot" => "MOONSHOT_API_KEY",
            "zhipu" => "ZHIPU_API_KEY",
            "volcengine" => "VOLCENGINE_API_KEY",
            "siliconflow" => "SILICONFLOW_API_KEY",
            _ => $"{provider.ToUpperInvariant()}_API_KEY"
        };
    }

    private string BuildErrorPage(ErrorInfo errorInfo, Exception exception)
    {
        var showDetails = _environment.IsDevelopment();
        var errorIcon = errorInfo.Type switch
        {
            ErrorType.Configuration => "&#9888;",
            ErrorType.Network => "&#128246;",
            _ => "&#10060;"
        };

        return $@"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>错误 - NanoBot</title>
    <style>
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 20px;
        }}
        .error-container {{
            background: white;
            border-radius: 16px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            max-width: 600px;
            width: 100%;
            padding: 40px;
            text-align: center;
        }}
        .error-icon {{
            font-size: 64px;
            margin-bottom: 20px;
        }}
        .error-title {{
            font-size: 24px;
            font-weight: 600;
            color: #1a1a2e;
            margin-bottom: 16px;
        }}
        .error-message {{
            background: #f8f9fa;
            border-left: 4px solid #667eea;
            padding: 16px 20px;
            border-radius: 8px;
            text-align: left;
            color: #4a4a5a;
            font-size: 14px;
            line-height: 1.6;
            white-space: pre-line;
            margin-bottom: 24px;
        }}
        .error-actions {{
            display: flex;
            gap: 12px;
            justify-content: center;
            flex-wrap: wrap;
        }}
        .btn {{
            padding: 12px 24px;
            border-radius: 8px;
            font-size: 14px;
            font-weight: 500;
            cursor: pointer;
            transition: all 0.2s;
            text-decoration: none;
            display: inline-flex;
            align-items: center;
            gap: 8px;
        }}
        .btn-primary {{
            background: #667eea;
            color: white;
            border: none;
        }}
        .btn-primary:hover {{
            background: #5a6fd6;
        }}
        .btn-secondary {{
            background: #f8f9fa;
            color: #4a4a5a;
            border: 1px solid #e0e0e0;
        }}
        .btn-secondary:hover {{
            background: #e9ecef;
        }}
        .details-section {{
            margin-top: 24px;
            padding-top: 24px;
            border-top: 1px solid #e0e0e0;
            text-align: left;
        }}
        .details-title {{
            font-size: 14px;
            font-weight: 600;
            color: #6c757d;
            margin-bottom: 12px;
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }}
        .details-content {{
            background: #f8f9fa;
            border-radius: 8px;
            padding: 16px;
            font-family: 'Consolas', 'Monaco', monospace;
            font-size: 12px;
            color: #333;
            overflow-x: auto;
            max-height: 300px;
            overflow-y: auto;
        }}
        .stack-trace {{
            white-space: pre-wrap;
            word-break: break-word;
        }}
        .footer {{
            margin-top: 24px;
            font-size: 12px;
            color: #6c757d;
        }}
    </style>
</head>
<body>
    <div class=""error-container"">
        <div class=""error-icon"">{errorIcon}</div>
        <h1 class=""error-title"">{errorInfo.Title}</h1>
        <div class=""error-message"">{errorInfo.Message}</div>
        <div class=""error-actions"">
            <button class=""btn btn-primary"" onclick=""window.location.reload()"">
                &#8634; 刷新页面
            </button>
            <a href=""/"" class=""btn btn-secondary"">
                &#8962; 返回首页
            </a>
        </div>
        {(showDetails ? $@"
        <div class=""details-section"">
            <div class=""details-title"">技术详情（仅开发环境显示）</div>
            <div class=""details-content"">
                <div><strong>异常类型：</strong>{exception.GetType().FullName}</div>
                <div><strong>异常消息：</strong>{exception.Message}</div>
                <div class=""stack-trace"">{exception.StackTrace}</div>
            </div>
        </div>
        " : "")}
        <div class=""footer"">
            NanoBot WebUI &copy; {DateTime.Now.Year}
        </div>
    </div>
</body>
</html>";
    }
}

public record ErrorInfo(string Title, string Message, ErrorType Type, Exception OriginalException);

public enum ErrorType
{
    Configuration,
    Network,
    Unknown
}

public static class UserFriendlyExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseUserFriendlyExceptions(this IApplicationBuilder app)
    {
        return app.UseMiddleware<UserFriendlyExceptionMiddleware>();
    }
}
