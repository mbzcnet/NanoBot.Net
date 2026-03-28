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

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        var contextBuilder = new StringBuilder();

        if (_memoryStore != null)
        {
            try
            {
                var memoryContent = await _memoryStore.LoadAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(memoryContent))
                {
                    contextBuilder.AppendLine("## Long-term Memory");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine(memoryContent.TrimEnd());
                    contextBuilder.AppendLine();
                }

                // Add history context if enabled
                var historyContent = _memoryStore.GetHistoryContext();
                if (!string.IsNullOrWhiteSpace(historyContent))
                {
                    contextBuilder.AppendLine(historyContent.TrimEnd());
                    contextBuilder.AppendLine();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load memory from IMemoryStore");
            }
        }

        // Fallback to direct file read if IMemoryStore is not available
        if (_memoryStore == null || string.IsNullOrEmpty(contextBuilder.ToString()))
        {
            var memoryPath = _workspace.GetMemoryFile();

            if (File.Exists(memoryPath))
            {
                try
                {
                    var memoryContent = await File.ReadAllTextAsync(memoryPath, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(memoryContent))
                    {
                        contextBuilder.AppendLine("## Long-term Memory");
                        contextBuilder.AppendLine();
                        contextBuilder.AppendLine(memoryContent.TrimEnd());
                        contextBuilder.AppendLine();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to read memory file: {MemoryPath}", memoryPath);
                }
            }

            // Also try to read history file directly
            var historyPath = _workspace.GetHistoryFile();
            if (File.Exists(historyPath))
            {
                try
                {
                    var historyContent = await File.ReadAllTextAsync(historyPath, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(historyContent))
                    {
                        contextBuilder.AppendLine("## Recent History");
                        contextBuilder.AppendLine();
                        contextBuilder.AppendLine(historyContent.TrimEnd());
                        contextBuilder.AppendLine();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to read history file: {HistoryPath}", historyPath);
                }
            }
        }

        var finalContent = contextBuilder.ToString().TrimEnd();
        if (string.IsNullOrWhiteSpace(finalContent))
        {
            return new AIContext();
        }

        return new AIContext
        {
            Instructions = finalContent
        };
    }

    protected override async ValueTask StoreAIContextAsync(
        InvokedContext context,
        CancellationToken cancellationToken)
    {
    }
}
