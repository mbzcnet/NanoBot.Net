using NanoBot.Core.Channels;
using NanoBot.Core.Channels.Accounts;

using NanoBot.Core.Bus;

namespace NanoBot.Core.Security;

/// <summary>
/// Security policy interface for access control.
/// </summary>
public interface ISecurityPolicy
{
    /// <summary>
    /// Evaluates a security context and returns a decision.
    /// </summary>
    Task<SecurityDecision> EvaluateAsync(
        SecurityContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the policy configuration.
    /// </summary>
    SecurityPolicyConfig Config { get; }
}

/// <summary>
/// Security context containing request information.
/// </summary>
public class SecurityContext
{
    /// <summary>
    /// The channel account being accessed.
    /// </summary>
    public required ChannelAccount Account { get; init; }

    /// <summary>
    /// The incoming message.
    /// </summary>
    public required InboundMessage Message { get; init; }

    /// <summary>
    /// Source of the message.
    /// </summary>
    public MessageSource Source { get; init; } = MessageSource.Unknown;

    /// <summary>
    /// Target user ID if applicable.
    /// </summary>
    public string? TargetUserId { get; init; }

    /// <summary>
    /// Target group ID if applicable.
    /// </summary>
    public string? TargetGroupId { get; init; }
}

/// <summary>
/// Security decision result.
/// </summary>
public enum SecurityDecision
{
    /// <summary>
    /// Request is allowed.
    /// </summary>
    Allow,

    /// <summary>
    /// Request is denied.
    /// </summary>
    Deny,

    /// <summary>
    /// Request is allowed but with a warning.
    /// </summary>
    AllowWithWarning
}

/// <summary>
/// Security policy configuration.
/// </summary>
public class SecurityPolicyConfig
{
    /// <summary>
    /// Direct message security rule.
    /// </summary>
    public SecurityRule? DirectMessage { get; set; }

    /// <summary>
    /// Group chat security rule.
    /// </summary>
    public SecurityRule? Group { get; set; }

    /// <summary>
    /// Channel security rule.
    /// </summary>
    public SecurityRule? Channel { get; set; }

    /// <summary>
    /// Default rule if no specific rule matches.
    /// </summary>
    public SecurityRule Default { get; set; } = SecurityRule.AllowAll;

    /// <summary>
    /// List of allowed sender IDs.
    /// </summary>
    public List<string>? AllowList { get; set; }

    /// <summary>
    /// List of blocked sender IDs.
    /// </summary>
    public List<string>? BlockList { get; set; }
}

/// <summary>
/// Security rule types.
/// </summary>
public enum SecurityRule
{
    /// <summary>
    /// Allow all requests.
    /// </summary>
    AllowAll,

    /// <summary>
    /// Deny all requests.
    /// </summary>
    DenyAll,

    /// <summary>
    /// Only allow users in the allow list.
    /// </summary>
    AllowList,

    /// <summary>
    /// Block users in the block list.
    /// </summary>
    BlockList,

    /// <summary>
    /// Only allow group admins.
    /// </summary>
    AdminOnly,

    /// <summary>
    /// Custom rule implementation required.
    /// </summary>
    Custom
}
