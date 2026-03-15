using NanoBot.Core.Bus;
using NanoBot.Core.Channels;
using NanoBot.Core.Channels.Accounts;
using NanoBot.Core.Security;
using Xunit;

namespace NanoBot.Channels.Tests;

public class SecurityPolicyTests
{
    [Fact]
    public async Task DefaultSecurityPolicy_WithAllowAll_ShouldAllowAll()
    {
        // Arrange
        var config = new SecurityPolicyConfig
        {
            Default = SecurityRule.AllowAll
        };
        var policy = new DefaultSecurityPolicy(config);
        
        var context = CreateSecurityContext("user123", MessageSource.DirectMessage);
        
        // Act
        var decision = await policy.EvaluateAsync(context);
        
        // Assert
        Assert.Equal(SecurityDecision.Allow, decision);
    }
    
    [Fact]
    public async Task DefaultSecurityPolicy_WithDenyAll_ShouldDenyAll()
    {
        // Arrange
        var config = new SecurityPolicyConfig
        {
            Default = SecurityRule.DenyAll
        };
        var policy = new DefaultSecurityPolicy(config);
        
        var context = CreateSecurityContext("user123", MessageSource.DirectMessage);
        
        // Act
        var decision = await policy.EvaluateAsync(context);
        
        // Assert
        Assert.Equal(SecurityDecision.Deny, decision);
    }
    
    [Fact]
    public async Task DefaultSecurityPolicy_WithAllowList_ShouldAllowMatchingUser()
    {
        // Arrange
        var config = new SecurityPolicyConfig
        {
            Default = SecurityRule.AllowList,
            AllowList = new List<string> { "allowedUser1", "allowedUser2" }
        };
        var policy = new DefaultSecurityPolicy(config);
        
        var context = CreateSecurityContext("allowedUser1", MessageSource.DirectMessage);
        
        // Act
        var decision = await policy.EvaluateAsync(context);
        
        // Assert
        Assert.Equal(SecurityDecision.Allow, decision);
    }
    
    [Fact]
    public async Task DefaultSecurityPolicy_WithAllowList_ShouldDenyNonMatchingUser()
    {
        // Arrange
        var config = new SecurityPolicyConfig
        {
            Default = SecurityRule.AllowList,
            AllowList = new List<string> { "allowedUser1" }
        };
        var policy = new DefaultSecurityPolicy(config);
        
        var context = CreateSecurityContext("deniedUser", MessageSource.DirectMessage);
        
        // Act
        var decision = await policy.EvaluateAsync(context);
        
        // Assert
        Assert.Equal(SecurityDecision.Deny, decision);
    }
    
    [Fact]
    public async Task DefaultSecurityPolicy_WithAllowList_WildcardShouldAllowAll()
    {
        // Arrange
        var config = new SecurityPolicyConfig
        {
            Default = SecurityRule.AllowList,
            AllowList = new List<string> { "*" }
        };
        var policy = new DefaultSecurityPolicy(config);
        
        var context = CreateSecurityContext("anyUser", MessageSource.DirectMessage);
        
        // Act
        var decision = await policy.EvaluateAsync(context);
        
        // Assert
        Assert.Equal(SecurityDecision.Allow, decision);
    }
    
    [Fact]
    public async Task DefaultSecurityPolicy_WithBlockList_ShouldBlockMatchingUser()
    {
        // Arrange
        var config = new SecurityPolicyConfig
        {
            Default = SecurityRule.BlockList,
            BlockList = new List<string> { "blockedUser" }
        };
        var policy = new DefaultSecurityPolicy(config);
        
        var context = CreateSecurityContext("blockedUser", MessageSource.DirectMessage);
        
        // Act
        var decision = await policy.EvaluateAsync(context);
        
        // Assert
        Assert.Equal(SecurityDecision.Deny, decision);
    }
    
    [Fact]
    public async Task DefaultSecurityPolicy_WithBlockList_ShouldAllowNonBlockedUser()
    {
        // Arrange
        var config = new SecurityPolicyConfig
        {
            Default = SecurityRule.BlockList,
            BlockList = new List<string> { "blockedUser" }
        };
        var policy = new DefaultSecurityPolicy(config);
        
        var context = CreateSecurityContext("normalUser", MessageSource.DirectMessage);
        
        // Act
        var decision = await policy.EvaluateAsync(context);
        
        // Assert
        Assert.Equal(SecurityDecision.Allow, decision);
    }
    
    [Fact]
    public async Task DefaultSecurityPolicy_WithDifferentSource_ShouldUseSpecificRule()
    {
        // Arrange
        var config = new SecurityPolicyConfig
        {
            DirectMessage = SecurityRule.DenyAll,
            Group = SecurityRule.AllowAll,
            Default = SecurityRule.AllowAll
        };
        var policy = new DefaultSecurityPolicy(config);
        
        // Test DM - should deny
        var dmContext = CreateSecurityContext("user123", MessageSource.DirectMessage);
        var dmDecision = await policy.EvaluateAsync(dmContext);
        
        // Test Group - should allow
        var groupContext = CreateSecurityContext("user123", MessageSource.Group);
        var groupDecision = await policy.EvaluateAsync(groupContext);
        
        // Assert
        Assert.Equal(SecurityDecision.Deny, dmDecision);
        Assert.Equal(SecurityDecision.Allow, groupDecision);
    }
    
    [Fact]
    public void SecurityContext_ShouldRequireAccountAndMessage()
    {
        // Arrange
        var account = new ChannelAccount
        {
            AccountId = "test",
            AccountName = "Test"
        };
        
        var message = new InboundMessage
        {
            Channel = "telegram",
            ChatId = "123",
            SenderId = "user123",
            Content = "Hello"
        };
        
        // Act
        var context = new SecurityContext
        {
            Account = account,
            Message = message,
            Source = MessageSource.DirectMessage
        };
        
        // Assert
        Assert.Equal(account, context.Account);
        Assert.Equal(message, context.Message);
        Assert.Equal(MessageSource.DirectMessage, context.Source);
    }
    
    [Fact]
    public void SecurityPolicyConfig_ShouldHaveDefaultAllowAll()
    {
        // Arrange & Act
        var config = new SecurityPolicyConfig();
        
        // Assert
        Assert.Equal(SecurityRule.AllowAll, config.Default);
    }
    
    private static SecurityContext CreateSecurityContext(string senderId, MessageSource source)
    {
        return new SecurityContext
        {
            Account = new ChannelAccount
            {
                AccountId = "test",
                AccountName = "Test"
            },
            Message = new InboundMessage
            {
                Channel = "telegram",
                ChatId = "123",
                SenderId = senderId,
                Content = "Test message"
            },
            Source = source
        };
    }
}
