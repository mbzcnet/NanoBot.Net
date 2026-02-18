using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Memory;
using NanoBot.Core.Workspace;

namespace NanoBot.Agent.Context;

public class MemoryContextProvider : AIContextProvider
{
    private readonly IWorkspaceManager _workspace;
    private readonly IMemoryStore? _memoryStore;
    private readonly ILogger<MemoryContextProvider>? _logger;

    public MemoryContextProvider(
        IWorkspaceManager workspace,
        IMemoryStore? memoryStore = null,
        ILogger<MemoryContextProvider>? logger = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _memoryStore = memoryStore;
        _logger = logger;
    }

    public override JsonElement Serialize(JsonSerializerOptions? options = null)
    {
        return JsonDocument.Parse("{}").RootElement.Clone();
    }

    protected override async ValueTask<AIContext> InvokingCoreAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        string? memoryContent = null;

        if (_memoryStore != null)
        {
            try
            {
                memoryContent = await _memoryStore.LoadAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load memory from IMemoryStore");
            }
        }

        if (string.IsNullOrEmpty(memoryContent))
        {
            var memoryPath = _workspace.GetMemoryFile();

            if (!File.Exists(memoryPath))
            {
                return new AIContext();
            }

            try
            {
                memoryContent = await File.ReadAllTextAsync(memoryPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read memory file: {MemoryPath}", memoryPath);
                return new AIContext();
            }
        }

        if (string.IsNullOrWhiteSpace(memoryContent))
        {
            return new AIContext();
        }

        return new AIContext
        {
            Instructions = $"## Memory\n\n{memoryContent}"
        };
    }

    protected override async ValueTask InvokedCoreAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
        if (context.InvokeException is not null)
        {
            return;
        }

        if (_memoryStore != null)
        {
            try
            {
                await _memoryStore.UpdateAsync(
                    context.RequestMessages,
                    context.ResponseMessages ?? [],
                    cancellationToken);

                _logger?.LogDebug("Memory updated after agent invocation");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to update memory after agent invocation");
            }
        }
    }
}
