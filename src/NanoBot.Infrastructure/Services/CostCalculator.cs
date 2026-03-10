using Microsoft.Extensions.Logging;
using NanoBot.Core.Messages;
using NanoBot.Core.Services;

namespace NanoBot.Infrastructure.Services;

/// <summary>
/// 成本计算器实现
/// </summary>
public class CostCalculator : ICostCalculator
{
    private readonly ILogger<CostCalculator>? _logger;
    private static readonly Dictionary<string, ModelPricing> _pricing = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI 模型
        ["gpt-4"] = new() { InputPrice = 0.03m, OutputPrice = 0.06m },
        ["gpt-4-turbo"] = new() { InputPrice = 0.01m, OutputPrice = 0.03m },
        ["gpt-4o"] = new() { InputPrice = 0.005m, OutputPrice = 0.015m },
        ["gpt-4o-mini"] = new() { InputPrice = 0.00015m, OutputPrice = 0.0006m },
        ["gpt-3.5-turbo"] = new() { InputPrice = 0.0005m, OutputPrice = 0.0015m },

        // Anthropic 模型
        ["claude-3-opus"] = new() { InputPrice = 0.015m, OutputPrice = 0.075m },
        ["claude-3-sonnet"] = new() { InputPrice = 0.003m, OutputPrice = 0.015m },
        ["claude-3-haiku"] = new() { InputPrice = 0.00025m, OutputPrice = 0.00125m },
        ["claude-3-5-sonnet"] = new() { InputPrice = 0.003m, OutputPrice = 0.015m },

        // Google 模型
        ["gemini-pro"] = new() { InputPrice = 0.0005m, OutputPrice = 0.0015m },
        ["gemini-ultra"] = new() { InputPrice = 0.0035m, OutputPrice = 0.0105m },

        // 其他常用模型
        ["llama-3"] = new() { InputPrice = 0.0001m, OutputPrice = 0.0002m },
        ["llama-3.1"] = new() { InputPrice = 0.0001m, OutputPrice = 0.0002m },
        ["mistral-large"] = new() { InputPrice = 0.002m, OutputPrice = 0.006m },
    };

    public CostCalculator(ILogger<CostCalculator>? logger = null)
    {
        _logger = logger;
    }

    public CostInfo CalculateCost(TokenUsage usage, ModelInfo model)
    {
        var pricing = GetPricing(model.ModelId);
        if (pricing == null)
        {
            _logger?.LogWarning("No pricing found for model {ModelId}, returning zero cost", model.ModelId);
            return new CostInfo { InputCost = 0, OutputCost = 0 };
        }

        // 计算成本（价格是基于每 1K tokens）
        var inputCost = (usage.Input / 1000m) * pricing.InputPrice;
        var outputCost = (usage.Output / 1000m) * pricing.OutputPrice;
        var reasoningCost = usage.Reasoning.HasValue && pricing.ReasoningPrice.HasValue
            ? (usage.Reasoning.Value / 1000m) * pricing.ReasoningPrice.Value
            : 0m;

        // 缓存成本（如果适用）
        var cacheReadCost = usage.Cache?.Read > 0 && pricing.CacheReadPrice.HasValue
            ? (usage.Cache.Read / 1000m) * pricing.CacheReadPrice.Value
            : 0m;
        var cacheWriteCost = usage.Cache?.Write > 0 && pricing.CacheWritePrice.HasValue
            ? (usage.Cache.Write / 1000m) * pricing.CacheWritePrice.Value
            : 0m;

        var totalCost = inputCost + outputCost + reasoningCost + cacheReadCost + cacheWriteCost;

        _logger?.LogDebug(
            "Cost calculation for {ModelId}: Input=${InputCost:F6}, Output=${OutputCost:F6}, Total=${TotalCost:F6}",
            model.ModelId,
            inputCost,
            outputCost,
            totalCost);

        return new CostInfo
        {
            InputCost = inputCost,
            OutputCost = outputCost,
            TotalCost = totalCost
        };
    }

    public ModelPricing? GetPricing(string modelId)
    {
        // 尝试直接匹配
        if (_pricing.TryGetValue(modelId, out var pricing))
        {
            return pricing;
        }

        // 尝试部分匹配（模型 ID 可能包含版本信息）
        foreach (var kvp in _pricing)
        {
            if (modelId.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Contains(modelId, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    public void SetPricing(string modelId, ModelPricing pricing)
    {
        _pricing[modelId] = pricing;
        _logger?.LogInformation("Updated pricing for model {ModelId}", modelId);
    }
}
