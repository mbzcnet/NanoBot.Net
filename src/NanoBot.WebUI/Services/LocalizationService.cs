using Microsoft.Extensions.Localization;
using Blazored.LocalStorage;
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
    private readonly ISyncLocalStorageService _localStorage;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string StorageKey = "nanobot_language";

    public LocalizationService(
        ISyncLocalStorageService localStorage,
        IHttpContextAccessor httpContextAccessor)
    {
        _localStorage = localStorage;
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetSavedCulture()
    {
        // 在服务端渲染时，localStorage 可能不可用，使用 try-catch 保护
        try
        {
            var savedCulture = _localStorage.GetItem<string>(StorageKey);
            return savedCulture ?? "auto";
        }
        catch (InvalidOperationException)
        {
            // Blazored.LocalStorage 在 SSR 时不可用，返回默认值
            return "auto";
        }
    }

    public void SaveCulture(string culture)
    {
        _localStorage.SetItem(StorageKey, culture);

        // 同时设置 Cookie，以便服务器端本地化能够读取
        SetCultureCookie(culture);
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
        SaveCulture(culture);
    }

    public Task<string> GetSavedCultureAsync()
    {
        return Task.FromResult(GetSavedCulture());
    }

    public Task SaveCultureAsync(string culture)
    {
        SaveCulture(culture);
        return Task.CompletedTask;
    }
}