using System.CommandLine;
using System.Text.Json;
using NanoBot.Core.Configuration;
using NanoBot.Core.Workspace;

namespace NanoBot.Cli.Commands;

public class SessionCommand : ICliCommand
{
    public string Name => "session";
    public string Description => "Session management";

    public async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var listOption = new Option<bool>(
            name: "--list",
            description: "List all sessions",
            getDefaultValue: () => false
        );
        listOption.AddAlias("-l");

        var clearOption = new Option<string?>(
            name: "--clear",
            description: "Clear specific session"
        );

        var clearAllOption = new Option<bool>(
            name: "--clear-all",
            description: "Clear all sessions",
            getDefaultValue: () => false
        );

        var exportOption = new Option<string?>(
            name: "--export",
            description: "Export session to file"
        );

        var configOption = new Option<string?>(
            name: "--config",
            description: "Configuration file path"
        );
        configOption.AddAlias("-c");

        var command = new Command(Name, Description)
        {
            listOption,
            clearOption,
            clearAllOption,
            exportOption,
            configOption
        };

        command.SetHandler(async (list, clear, clearAll, export, configPath) =>
        {
            await ExecuteSessionAsync(list, clear, clearAll, export, configPath, cancellationToken);
        }, listOption, clearOption, clearAllOption, exportOption, configOption);

        return await command.InvokeAsync(args);
    }

    private async Task ExecuteSessionAsync(
        bool list,
        string? clear,
        bool clearAll,
        string? export,
        string? configPath,
        CancellationToken cancellationToken)
    {
        var config = await ConfigurationLoader.LoadWithDefaultsAsync(configPath, cancellationToken);
        var sessionsPath = config.Workspace.GetSessionsPath();

        if (!string.IsNullOrEmpty(clear))
        {
            await ClearSessionAsync(sessionsPath, clear);
            return;
        }

        if (clearAll)
        {
            await ClearAllSessionsAsync(sessionsPath);
            return;
        }

        if (!string.IsNullOrEmpty(export))
        {
            await ExportSessionAsync(sessionsPath, export);
            return;
        }

        await ListSessionsAsync(sessionsPath);
    }

    private static Task ListSessionsAsync(string sessionsPath)
    {
        if (!Directory.Exists(sessionsPath))
        {
            Console.WriteLine("No sessions found.");
            return Task.CompletedTask;
        }

        var sessionFiles = Directory.GetFiles(sessionsPath, "*.json");

        if (sessionFiles.Length == 0)
        {
            Console.WriteLine("No sessions found.");
            return Task.CompletedTask;
        }

        Console.WriteLine("Sessions:\n");
        Console.WriteLine($"{"ID",-40} {"Last Modified",-20} {"Size"}");
        Console.WriteLine(new string('-', 80));

        foreach (var file in sessionFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var lastModified = File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm:ss");
            var size = new FileInfo(file).Length;
            var sizeStr = FormatSize(size);

            Console.WriteLine($"{fileName,-40} {lastModified,-20} {sizeStr}");
        }

        Console.WriteLine($"\nTotal: {sessionFiles.Length} session(s)");
        return Task.CompletedTask;
    }

    private static Task ClearSessionAsync(string sessionsPath, string sessionId)
    {
        var sessionFile = Path.Combine(sessionsPath, $"{sessionId}.json");

        if (!File.Exists(sessionFile))
        {
            Console.WriteLine($"Session not found: {sessionId}");
            return Task.CompletedTask;
        }

        File.Delete(sessionFile);
        Console.WriteLine($"✓ Session cleared: {sessionId}");
        return Task.CompletedTask;
    }

    private static Task ClearAllSessionsAsync(string sessionsPath)
    {
        if (!Directory.Exists(sessionsPath))
        {
            Console.WriteLine("No sessions to clear.");
            return Task.CompletedTask;
        }

        var sessionFiles = Directory.GetFiles(sessionsPath, "*.json");
        var count = 0;

        foreach (var file in sessionFiles)
        {
            File.Delete(file);
            count++;
        }

        Console.WriteLine($"✓ Cleared {count} session(s)");
        return Task.CompletedTask;
    }

    private static async Task ExportSessionAsync(string sessionsPath, string sessionId)
    {
        var sessionFile = Path.Combine(sessionsPath, $"{sessionId}.json");

        if (!File.Exists(sessionFile))
        {
            Console.WriteLine($"Session not found: {sessionId}");
            return;
        }

        var content = await File.ReadAllTextAsync(sessionFile);
        var exportPath = $"{sessionId}_export.json";

        await File.WriteAllTextAsync(exportPath, content);
        Console.WriteLine($"✓ Session exported to: {exportPath}");
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        var order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}
