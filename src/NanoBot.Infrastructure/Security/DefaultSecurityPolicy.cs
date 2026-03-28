using NanoBot.Core.Bus;
using NanoBot.Core.Channels;
using NanoBot.Core.Security;

namespace NanoBot.Infrastructure.Security;

/// <summary>
/// Default implementation of security policy.
/// </summary>
public class DefaultSecurityPolicy : ISecurityPolicy
{
    public SecurityPolicyConfig Config { get; }

    public DefaultSecurityPolicy(SecurityPolicyConfig config)
    {
        Config = config;
    }

    public Task<SecurityDecision> EvaluateAsync(
        SecurityContext context,
        CancellationToken cancellationToken = default)
    {
        var rule = GetRuleForSource(context.Source);
        var senderId = context.Message.SenderId;

        return rule switch
        {
            SecurityRule.AllowAll => Task.FromResult(SecurityDecision.Allow),
            SecurityRule.DenyAll => Task.FromResult(SecurityDecision.Deny),
            SecurityRule.AllowList => EvaluateAllowList(context),
            SecurityRule.BlockList => EvaluateBlockList(context),
            SecurityRule.AdminOnly => EvaluateAdminOnly(context, cancellationToken),
            _ => Task.FromResult(SecurityDecision.Allow)
        };
    }

    private SecurityRule GetRuleForSource(MessageSource source) => source switch
    {
        MessageSource.DirectMessage => Config.DirectMessage ?? Config.Default,
        MessageSource.Group => Config.Group ?? Config.Default,
        MessageSource.Channel => Config.Channel ?? Config.Default,
        _ => Config.Default
    };

    private Task<SecurityDecision> EvaluateAllowList(SecurityContext context)
    {
        var allowed = Config.AllowList ?? new List<string>();
        if (allowed.Contains("*") || allowed.Contains(context.Message.SenderId))
            return Task.FromResult(SecurityDecision.Allow);

        return Task.FromResult(SecurityDecision.Deny);
    }

    private Task<SecurityDecision> EvaluateBlockList(SecurityContext context)
    {
        var blocked = Config.BlockList ?? new List<string>();
        if (blocked.Contains(context.Message.SenderId))
            return Task.FromResult(SecurityDecision.Deny);

        return Task.FromResult(SecurityDecision.Allow);
    }

    private async Task<SecurityDecision> EvaluateAdminOnly(SecurityContext context, CancellationToken cancellationToken)
    {
        if (context.Source != MessageSource.Group)
            return SecurityDecision.Allow;

        if (string.IsNullOrEmpty(context.TargetGroupId) || string.IsNullOrEmpty(context.Message.SenderId))
            return SecurityDecision.Deny;

        return await EvaluateAllowList(context);
    }
}
