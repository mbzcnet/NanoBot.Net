using NanoBot.Core.Messages;

namespace NanoBot.Core.Services;

/// <summary>
/// 成本计算器接口
/// </summary>
public interface ICostCalculator
{
    /// <summary>
    /// 计算 Token 使用成本
    /// </summary>
    /// <param name="usage">Token 使用情况</param>
    /// <param name="model">模型信息</param>
    /// <returns>成本信息</returns>
    CostInfo CalculateCost(TokenUsage usage, ModelInfo model);

    /// <summary>
    /// 获取模型定价（每 1K tokens）
    /// </summary>
    /// <param name="modelId">模型 ID</param>
    /// <returns>定价信息，如果未找到则返回 null</returns>
    ModelPricing? GetPricing(string modelId);

    /// <summary>
    /// 注册或更新模型定价
    /// </summary>
    /// <param name="modelId">模型 ID</param>
    /// <param name="pricing">定价信息</param>
    void SetPricing(string modelId, ModelPricing pricing);
}

/// <summary>
/// 模型定价信息
/// </summary>
public record ModelPricing
{
    /// <summary>
    /// 输入 Token 价格（每 1K tokens，美元）
    /// </summary>
    public required decimal InputPrice { get; init; }

    /// <summary>
    /// 输出 Token 价格（每 1K tokens，美元）
    /// </summary>
    public required decimal OutputPrice { get; init; }

    /// <summary>
    /// 缓存读取 Token 价格（每 1K tokens，美元）
    /// </summary>
    public decimal? CacheReadPrice { get; init; }

    /// <summary>
    /// 缓存写入 Token 价格（每 1K tokens，美元）
    /// </summary>
    public decimal? CacheWritePrice { get; init; }

    /// <summary>
    /// 推理 Token 价格（每 1K tokens，美元）
    /// </summary>
    public decimal? ReasoningPrice { get; init; }

    /// <summary>
    /// 货币单位
    /// </summary>
    public string Currency { get; init; } = "USD";
}
