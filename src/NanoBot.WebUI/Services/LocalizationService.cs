using Microsoft.Extensions.Localization;
using Blazored.LocalStorage;

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
        // 首先尝试从 HTTP 上下文获取文化设置（SSR 场景）
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            // 尝试从请求的文化特性中获取
            var requestCulture = httpContext.Features.Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>();
            if (requestCulture != null)
            {
                return requestCulture.RequestCulture.Culture.Name;
            }
        }

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