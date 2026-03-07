using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Http;

namespace NanoBot.WebUI.Services;

public interface ILocalizationService
{
    Task SetCurrentCultureAsync(string culture);
    Task<string> GetCurrentCultureAsync();
    Task<string> GetSavedCultureAsync();
    Task SaveCultureAsync(string culture);
    string GetSavedCulture();
}

public class LocalizationService : ILocalizationService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string StorageKey = "nanobot_language";
    private const string JsFunctionName = "nanobot.setCulture";

    public LocalizationService(
        IJSRuntime jsRuntime,
        IHttpContextAccessor httpContextAccessor)
    {
        _jsRuntime = jsRuntime;
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetSavedCulture()
    {
        // 尝试从 Cookie 读取保存的语言设置（服务端渲染时使用）
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            var cookieCulture = httpContext.Request.Cookies[".AspNetCore.Culture"];
            if (!string.IsNullOrEmpty(cookieCulture))
            {
                // Cookie format: "c=zh-CN|u=zh-CN"
                var parts = cookieCulture.Split('|');
                foreach (var part in parts)
                {
                    if (part.StartsWith("c="))
                    {
                        return part.Substring(2);
                    }
                }
            }
        }
        return "auto";
    }

    public void SaveCulture(string culture)
    {
        // 同步版本不再使用，保持向后兼容性
        // 实际保存通过异步方法 SaveCultureAsync 完成
    }

    public async Task<string> GetCurrentCultureAsync()
    {
        var savedCulture = GetSavedCulture();

        if (savedCulture != "auto")
        {
            return savedCulture;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            var requestCulture = httpContext.Features.Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>();
            if (requestCulture != null)
            {
                return requestCulture.RequestCulture.Culture.Name;
            }
        }

        return "zh-CN";
    }

    public async Task SetCurrentCultureAsync(string culture)
    {
        await SaveCultureAsync(culture);
    }

    public Task<string> GetSavedCultureAsync()
    {
        return Task.FromResult(GetSavedCulture());
    }

    public async Task SaveCultureAsync(string culture)
    {
        // 使用 JSInterop 调用浏览器端的函数保存语言设置
        // 这会在浏览器的 localStorage 和 Cookie 中保存设置
        try
        {
            await _jsRuntime.InvokeVoidAsync(JsFunctionName, culture);
        }
        catch (Exception ex)
        {
            // 如果 JS 调用失败（如初始加载时），回退到设置 Cookie
            Console.WriteLine($"Failed to save culture via JS: {ex.Message}");
            SetCultureCookie(culture);
        }
    }

    private void SetCultureCookie(string culture)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            // 设置 ASP.NET Core 本地化中间件能够识别的 Cookie 格式
            httpContext.Response.Cookies.Append(
                ".AspNetCore.Culture",
                $"c={culture}|u={culture}",
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    SameSite = SameSiteMode.Lax,
                    Path = "/"
                });
        }
    }
}