using NanoBot.Core.Configuration;

namespace NanoBot.WebUI.Services;

/// <summary>
/// WebUI 配置服务接口
/// 用于加载和保存 WebUI 设置（独立于 Agent 配置）
/// </summary>
public interface IWebUIConfigService
{
    /// <summary>
    /// 获取 WebUI 配置
    /// </summary>
    WebUIConfig GetConfig();

    /// <summary>
    /// 保存 WebUI 配置
    /// </summary>
    /// <param name="config">配置对象</param>
    /// <returns>是否保存成功</returns>
    Task<bool> SaveConfigAsync(WebUIConfig config);

    /// <summary>
    /// 获取配置文件路径
    /// </summary>
    string GetConfigPath();
}
