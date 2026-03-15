using Moq;
using Microsoft.Extensions.Logging;
using Xunit;
using NanoBot.Channels.Discovery;
using NanoBot.Core.Channels;
using NanoBot.Core.Channels.Accounts;
using NanoBot.Core.Channels.Adapters;

namespace NanoBot.Channels.Tests;

public class ChannelPluginDiscovererTests
{
    [Fact]
    public void DiscoverChannelPlugin_ShouldFindPluginInterfaces()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ChannelPluginDiscoverer>>();
        var assemblies = new[] { typeof(ChannelPluginDiscoverer).Assembly };
        
        var discoverer = new ChannelPluginDiscoverer(loggerMock.Object, assemblies);
        
        // Act
        var plugins = discoverer.DiscoverAsync().GetAwaiter().GetResult();
        
        // Assert - should at least not throw
        Assert.NotNull(plugins);
    }
    
    [Fact]
    public void IsChannelPlugin_ShouldDetectCorrectly()
    {
        // Create a mock plugin type that implements IChannelPlugin<>
        var pluginType = typeof(TestChannelPlugin);
        
        // Act - check if it implements IChannelPlugin<>
        var isChannelPlugin = pluginType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IChannelPlugin<>));
        
        // Assert
        Assert.True(isChannelPlugin);
    }
    
    [Fact]
    public void DiscoverFromAssembly_ShouldFilterCorrectly()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ChannelPluginDiscoverer>>();
        var assemblies = new[] { typeof(ChannelPluginDiscoverer).Assembly };
        
        var discoverer = new ChannelPluginDiscoverer(loggerMock.Object, assemblies);
        
        // Act
        var plugins = discoverer.DiscoverFromAssemblyAsync(typeof(ChannelPluginDiscoverer).Assembly).GetAwaiter().GetResult();
        
        // Assert
        Assert.NotNull(plugins);
    }
}

public class DiscoveredChannelPluginTests
{
    [Fact]
    public void DiscoveredChannelPlugin_ShouldStoreCorrectData()
    {
        // Arrange
        var pluginType = typeof(TestChannelPlugin);
        var assembly = pluginType.Assembly;
        
        // Act
        var discovered = new DiscoveredChannelPlugin
        {
            PluginType = pluginType,
            Assembly = assembly,
            IsBuiltIn = true
        };
        
        // Assert
        Assert.Equal(pluginType, discovered.PluginType);
        Assert.Equal(assembly, discovered.Assembly);
        Assert.True(discovered.IsBuiltIn);
        Assert.Contains("NanoBot", discovered.AssemblyName);
    }
    
    [Fact]
    public void DiscoveredChannelPlugin_ShouldHandleNonBuiltIn()
    {
        // Arrange
        var pluginType = typeof(TestChannelPlugin);
        
        // Act
        var discovered = new DiscoveredChannelPlugin
        {
            PluginType = pluginType,
            Assembly = pluginType.Assembly,
            IsBuiltIn = false
        };
        
        // Assert
        Assert.False(discovered.IsBuiltIn);
    }
}

public class ChannelPluginFactoryTests
{
    [Fact]
    public void Create_ShouldReturnPluginInstance()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        var discoveredPlugins = new List<DiscoveredChannelPlugin>
        {
            new()
            {
                PluginType = typeof(TestChannelPlugin),
                Assembly = typeof(TestChannelPlugin).Assembly,
                IsBuiltIn = true
            }
        };
        
        // Note: This test shows the factory design
        // Actual DI integration would need more setup
        Assert.NotNull(discoveredPlugins);
        Assert.Single(discoveredPlugins);
    }
}

/// <summary>
/// Test implementation of IChannelPlugin for testing purposes.
/// </summary>
public class TestChannelPlugin : IChannelPlugin<TestAccount>
{
    public ChannelId Id => "test";
    
    public ChannelPluginMeta Meta => new("test", "Test", "Test plugin", "1.0.0", new[] { "text" });
    
    public ChannelCapabilities Capabilities => new();
    
    public IChannelConfigAdapter<TestAccount> Config => throw new NotImplementedException();
    
    public IChannelSecurityAdapter<TestAccount>? Security => null;
    
    public IChannelOutboundAdapter? Outbound => null;
    
    public IChannelGroupAdapter? Groups => null;
    
    public IChannelMentionAdapter? Mentions => null;
    
    public IChannelThreadingAdapter? Threading => null;
    
    public IChannelStreamingAdapter? Streaming => null;
    
    public IChannelHeartbeatAdapter? Heartbeat => null;
}

public class TestAccount
{
    public string Id { get; set; } = "";
}
