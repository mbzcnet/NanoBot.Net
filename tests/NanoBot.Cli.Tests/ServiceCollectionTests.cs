using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using NanoBot.Agent;
using NanoBot.Cli.Extensions;
using NanoBot.Core.Bus;
using NanoBot.Core.Channels;
using NanoBot.Core.Configuration;
using NanoBot.Core.Cron;
using NanoBot.Core.Heartbeat;
using NanoBot.Core.Memory;
using NanoBot.Core.Skills;
using NanoBot.Core.Subagents;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Bus;
using NanoBot.Infrastructure.Resources;
using NanoBot.Infrastructure.Workspace;
using NanoBot.Providers;
using Xunit;

namespace NanoBot.Cli.Tests;

public class ServiceCollectionTests
{
    [Fact]
    public void AddNanoBotConfiguration_ShouldRegisterConfigurationServices()
    {
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();

        services.AddNanoBotConfiguration(configuration);

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IConfiguration>());
        Assert.NotNull(serviceProvider.GetService<AgentConfig>());
        Assert.NotNull(serviceProvider.GetService<WorkspaceConfig>());
        Assert.NotNull(serviceProvider.GetService<LlmConfig>());
        Assert.NotNull(serviceProvider.GetService<ChannelsConfig>());
        Assert.NotNull(serviceProvider.GetService<MemoryConfig>());
        Assert.NotNull(serviceProvider.GetService<SecurityConfig>());
    }

    [Fact]
    public void AddMicrosoftAgentsAI_ShouldRegisterChatClientServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var config = new LlmConfig
        {
            Provider = "openai",
            Model = "gpt-4o",
            ApiKey = "test-key"
        };

        services.AddMicrosoftAgentsAI(config);

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IChatClientFactory>());
        Assert.NotNull(serviceProvider.GetService<IChatClient>());
    }

    [Fact]
    public void AddNanoBotInfrastructure_ShouldRegisterInfrastructureServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddNanoBotInfrastructure();

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IWorkspaceManager>());
        Assert.NotNull(serviceProvider.GetService<IBootstrapLoader>());
        Assert.NotNull(serviceProvider.GetService<IMessageBus>());
    }

    [Fact]
    public void AddNanoBotContextProviders_ShouldRegisterMemoryStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new WorkspaceConfig());
        services.AddSingleton(new MemoryConfig());
        services.AddSingleton<IEmbeddedResourceLoader, EmbeddedResourceLoader>();
        services.AddSingleton<IWorkspaceManager>(sp => new WorkspaceManager(sp.GetService<WorkspaceConfig>()!, sp.GetService<IEmbeddedResourceLoader>()));

        services.AddNanoBotContextProviders();

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IMemoryStore>());
    }

    [Fact]
    public void AddNanoBotBackgroundServices_ShouldRegisterBackgroundServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new WorkspaceConfig());
        services.AddSingleton<IEmbeddedResourceLoader, EmbeddedResourceLoader>();
        services.AddSingleton<IWorkspaceManager>(sp => new WorkspaceManager(sp.GetService<WorkspaceConfig>()!, sp.GetService<IEmbeddedResourceLoader>()));
        services.AddSingleton<IMessageBus, MessageBus>();

        services.AddNanoBotBackgroundServices();

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<ICronService>());
        Assert.NotNull(serviceProvider.GetService<IHeartbeatService>());
        Assert.NotNull(serviceProvider.GetService<ISkillsLoader>());
        Assert.NotNull(serviceProvider.GetService<ISubagentManager>());
    }

    [Fact]
    public void AddNanoBotChannels_ShouldRegisterChannelManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageBus, MessageBus>();

        services.AddNanoBotChannels();

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IChannelManager>());
    }

    [Fact]
    public void AddNanoBot_ShouldRegisterAllServices()
    {
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();

        services.AddLogging();
        services.AddNanoBot(configuration);

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IConfiguration>());
        Assert.NotNull(serviceProvider.GetService<AgentConfig>());
        Assert.NotNull(serviceProvider.GetService<IWorkspaceManager>());
        Assert.NotNull(serviceProvider.GetService<IMessageBus>());
        Assert.NotNull(serviceProvider.GetService<ICronService>());
        Assert.NotNull(serviceProvider.GetService<IHeartbeatService>());
        Assert.NotNull(serviceProvider.GetService<ISkillsLoader>());
        Assert.NotNull(serviceProvider.GetService<IChannelManager>());
        Assert.NotNull(serviceProvider.GetService<IMemoryStore>());
        Assert.NotNull(serviceProvider.GetService<ISessionManager>());
        Assert.NotNull(serviceProvider.GetService<ChatClientAgent>());
        Assert.NotNull(serviceProvider.GetService<IAgentRuntime>());
    }

    [Fact]
    public void AddNanoBotCli_ShouldBuildConfigurationAndRegisterServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddNanoBotCli();

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IConfiguration>());
    }

    [Fact]
    public void Services_ShouldBeRegisteredAsSingleton()
    {
        var services = new ServiceCollection();
        var configuration = CreateTestConfiguration();

        services.AddLogging();
        services.AddNanoBot(configuration);

        var serviceProvider = services.BuildServiceProvider();

        var workspace1 = serviceProvider.GetRequiredService<IWorkspaceManager>();
        var workspace2 = serviceProvider.GetRequiredService<IWorkspaceManager>();
        Assert.Same(workspace1, workspace2);

        var bus1 = serviceProvider.GetRequiredService<IMessageBus>();
        var bus2 = serviceProvider.GetRequiredService<IMessageBus>();
        Assert.Same(bus1, bus2);

        var cron1 = serviceProvider.GetRequiredService<ICronService>();
        var cron2 = serviceProvider.GetRequiredService<ICronService>();
        Assert.Same(cron1, cron2);
    }

    private static IConfiguration CreateTestConfiguration()
    {
        var configDict = new Dictionary<string, string?>
        {
            ["Agent:Name"] = "TestAgent",
            ["Agent:Workspace:Path"] = "/tmp/test-workspace",
            ["Agent:Llm:Provider"] = "openai",
            ["Agent:Llm:Model"] = "gpt-4o",
            ["Agent:Llm:ApiKey"] = "test-api-key"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();
    }
}
