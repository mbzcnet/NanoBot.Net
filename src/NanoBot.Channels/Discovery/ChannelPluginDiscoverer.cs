using System.Reflection;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Channels;

namespace NanoBot.Channels.Discovery;

/// <summary>
/// Discovers channel plugins from assemblies.
/// </summary>
public interface IChannelPluginDiscoverer
{
    /// <summary>
    /// Discovers all channel plugins in available assemblies.
    /// </summary>
    Task<IReadOnlyList<DiscoveredChannelPlugin>> DiscoverAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Discovers plugins from a specific assembly.
    /// </summary>
    Task<IReadOnlyList<DiscoveredChannelPlugin>> DiscoverFromAssemblyAsync(
        Assembly assembly, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a discovered channel plugin.
/// </summary>
public class DiscoveredChannelPlugin
{
    /// <summary>
    /// The plugin type.
    /// </summary>
    public required Type PluginType { get; init; }
    
    /// <summary>
    /// The assembly containing the plugin.
    /// </summary>
    public required Assembly Assembly { get; init; }
    
    /// <summary>
    /// Assembly name for display.
    /// </summary>
    public string AssemblyName => Assembly.GetName().Name ?? "Unknown";
    
    /// <summary>
    /// Whether the plugin is built-in or external.
    /// </summary>
    public bool IsBuiltIn { get; init; }
}

/// <summary>
/// Default implementation of channel plugin discoverer.
/// </summary>
public class ChannelPluginDiscoverer : IChannelPluginDiscoverer
{
    private readonly ILogger<ChannelPluginDiscoverer> _logger;
    private readonly IEnumerable<Assembly> _assemblies;
    
    public ChannelPluginDiscoverer(
        ILogger<ChannelPluginDiscoverer> logger,
        IEnumerable<Assembly> assemblies)
    {
        _logger = logger;
        _assemblies = assemblies;
    }
    
    public async Task<IReadOnlyList<DiscoveredChannelPlugin>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var plugins = new List<DiscoveredChannelPlugin>();
        
        foreach (var assembly in _assemblies)
        {
            try
            {
                var discovered = await DiscoverFromAssemblyAsync(assembly, cancellationToken);
                plugins.AddRange(discovered);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to discover plugins from assembly {Assembly}", 
                    assembly.GetName().Name);
            }
        }
        
        _logger.LogInformation("Discovered {Count} channel plugins", plugins.Count);
        return plugins;
    }
    
    public Task<IReadOnlyList<DiscoveredChannelPlugin>> DiscoverFromAssemblyAsync(
        Assembly assembly, 
        CancellationToken cancellationToken = default)
    {
        var plugins = new List<DiscoveredChannelPlugin>();
        
        try
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => IsChannelPlugin(t));
            
            foreach (var type in types)
            {
                var isBuiltIn = type.Assembly.GetName().Name?.Contains("NanoBot") == true;
                
                plugins.Add(new DiscoveredChannelPlugin
                {
                    PluginType = type,
                    Assembly = assembly,
                    IsBuiltIn = isBuiltIn
                });
                
                _logger.LogDebug("Found channel plugin: {PluginType} in {Assembly}", 
                    type.Name, assembly.GetName().Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning assembly {Assembly} for plugins", 
                assembly.GetName().Name);
        }
        
        return Task.FromResult<IReadOnlyList<DiscoveredChannelPlugin>>(plugins);
    }
    
    private static bool IsChannelPlugin(Type type)
    {
        return type.GetInterfaces()
            .Any(i => i.IsGenericType && 
                     i.GetGenericTypeDefinition() == typeof(IChannelPlugin<>));
    }
}

/// <summary>
/// Plugin factory for creating channel plugin instances.
/// </summary>
public interface IChannelPluginFactory
{
    /// <summary>
    /// Creates a channel plugin instance.
    /// </summary>
    IChannelPlugin<TAccount>? Create<TAccount>(Type pluginType) where TAccount : class;
    
    /// <summary>
    /// Creates all registered channel plugins.
    /// </summary>
    IReadOnlyList<IChannelPlugin<TAccount>> CreateAll<TAccount>() where TAccount : class;
}

/// <summary>
/// Default implementation of channel plugin factory using DI.
/// </summary>
public class ChannelPluginFactory : IChannelPluginFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IEnumerable<DiscoveredChannelPlugin> _discoveredPlugins;
    
    public ChannelPluginFactory(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        IEnumerable<DiscoveredChannelPlugin> discoveredPlugins)
    {
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
        _discoveredPlugins = discoveredPlugins;
    }
    
    public IChannelPlugin<TAccount>? Create<TAccount>(Type pluginType) where TAccount : class
    {
        try
        {
            // Try to get from DI first
            var service = _serviceProvider.GetService(pluginType);
            if (service is IChannelPlugin<TAccount> plugin)
                return plugin;
            
            // Fall back toActivator
            return Activator.CreateInstance(pluginType) as IChannelPlugin<TAccount>;
        }
        catch
        {
            return null;
        }
    }
    
    public IReadOnlyList<IChannelPlugin<TAccount>> CreateAll<TAccount>() where TAccount : class
    {
        var plugins = new List<IChannelPlugin<TAccount>>();
        
        foreach (var discovered in _discoveredPlugins)
        {
            var plugin = Create<TAccount>(discovered.PluginType);
            if (plugin != null)
                plugins.Add(plugin);
        }
        
        return plugins;
    }
}
