using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Memory;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent.Context;

public class MemoryConsolidationContextProvider : AIContextProvider
{
    private readonly IChatClient _chatClient;
    private readonly IMemoryStore _memoryStore;
    private readonly IWorkspaceManager _workspace;
    private readonly int _memoryWindow;
    private readonly ILogger<MemoryConsolidationContextProvider>? _logger;

    public MemoryConsolidationContextProvider(
        IChatClient chatClient,
        IMemoryStore memoryStore,
        IWorkspaceManager workspace,
        int memoryWindow = 50,
        ILogger<MemoryConsolidationContextProvider>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _memoryWindow = memoryWindow;
        _logger = logger;
    }

    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        return JsonSerializer.SerializeToElement(new { _memoryWindow }, jsonSerializerOptions);
    }

    protected override ValueTask<AIContext> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<AIContext>(new AIContext());
    }
}
