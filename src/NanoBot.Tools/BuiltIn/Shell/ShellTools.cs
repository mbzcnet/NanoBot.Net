using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace NanoBot.Tools.BuiltIn;

public class ShellToolOptions
{
    public int Timeout { get; set; } = 60;
    public string? WorkingDirectory { get; set; }
    public bool RestrictToWorkspace { get; set; }
    public List<string>? AllowPatterns { get; set; }

    public static readonly string[] DefaultDenyPatterns =
    {
        @"\brm\s+-[rf]{1,2}\b",
        @"\bdel\s+/[fq]\b",
        @"\brmdir\s+/s\b",
        @"(?:^|[;&|]\s*)format\b",
        @"\b(mkfs|diskpart)\b",
        @"\bdd\s+if=",
        @">\s*/dev/sd",
        @"\b(shutdown|reboot|poweroff)\b",
        @":\(\)\s*\{.*\};\s*:",
    };
}

public static class ShellTools
{
    private const int MaxOutputLength = 10000;
    private const int PostKillWaitSeconds = 5;

    public static AITool CreateExecTool(ShellToolOptions? options = null)
    {
        options ??= new ShellToolOptions();
        var denyPatterns = ShellToolOptions.DefaultDenyPatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToList();
        var allowPatterns = options.AllowPatterns?
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToList();

        return AIFunctionFactory.Create(
            (string command, int timeoutSeconds, string? workingDir) => ExecuteAsync(
                command, timeoutSeconds, workingDir, options, denyPatterns, allowPatterns),
            new AIFunctionFactoryOptions
            {
                Name = "exec",
                Description = "Execute a shell command and return the output. Use with caution as this can be dangerous."
            });
    }

    public static AITool CreateExecTool(IEnumerable<string>? blockedCommands = null)
    {
        var options = new ShellToolOptions();
        var blocked = new HashSet<string>(blockedCommands ?? [], StringComparer.OrdinalIgnoreCase);

        return AIFunctionFactory.Create(
            (string command, int timeoutSeconds) => ExecuteSimpleAsync(command, timeoutSeconds, blocked),
            new AIFunctionFactoryOptions
            {
                Name = "exec",
                Description = "Execute a shell command and return the output. Use with caution as this can be dangerous."
            });
    }

    private static async Task<string> ExecuteSimpleAsync(
        string command,
        int timeoutSeconds,
        HashSet<string> blockedCommands)
    {
        var options = new ShellToolOptions();
        var denyPatterns = ShellToolOptions.DefaultDenyPatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToList();

        var guardError = GuardCommandSimple(command, blockedCommands, denyPatterns);
        if (guardError != null)
            return guardError;

        return await ExecuteCoreAsync(command, timeoutSeconds, options.WorkingDirectory);
    }

    private static async Task<string> ExecuteAsync(
        string command,
        int timeoutSeconds,
        string? workingDir,
        ShellToolOptions options,
        List<Regex> denyPatterns,
        List<Regex>? allowPatterns)
    {
        var cwd = workingDir ?? options.WorkingDirectory ?? Directory.GetCurrentDirectory();

        var guardError = GuardCommand(command, cwd, options, denyPatterns, allowPatterns);
        if (guardError != null)
            return guardError;

        return await ExecuteCoreAsync(command, timeoutSeconds, cwd);
    }

    private static async Task<string> ExecuteCoreAsync(
        string command,
        int timeoutSeconds,
        string? workingDirectory)
    {
        try
        {
            var timeout = timeoutSeconds > 0 ? timeoutSeconds : 30;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "/bin/sh",
                    Arguments = Environment.OSVersion.Platform == PlatformID.Win32NT
                        ? $"/c {command}"
                        : $"-c \"{command}\"",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);

                try
                {
                    using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(PostKillWaitSeconds));
                    await process.WaitForExitAsync(waitCts.Token);
                }
                catch (OperationCanceledException)
                {
                }

                return $"Error: Command timed out after {timeout} seconds";
            }

            var output = await outputTask;
            var error = await errorTask;

            var result = BuildResult(output, error, process.ExitCode);

            if (result.Length > MaxOutputLength)
            {
                result = result[..MaxOutputLength] + $"\n... (truncated, {result.Length - MaxOutputLength} more chars)";
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return "Error: Command timed out.";
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }

    private static string? GuardCommandSimple(
        string command,
        HashSet<string> blockedCommands,
        List<Regex> denyPatterns)
    {
        var lower = command.ToLowerInvariant().Trim();

        foreach (var pattern in denyPatterns)
        {
            if (pattern.IsMatch(lower))
            {
                return "Error: Command blocked by safety guard (dangerous pattern detected)";
            }
        }

        var parts = command.Split(' ', 2);
        var cmdName = parts[0];

        if (blockedCommands.Contains(cmdName))
        {
            return $"Error: Command '{cmdName}' is blocked for security reasons.";
        }

        return null;
    }

    private static string? GuardCommand(
        string command,
        string cwd,
        ShellToolOptions options,
        List<Regex> denyPatterns,
        List<Regex>? allowPatterns)
    {
        var lower = command.ToLowerInvariant().Trim();

        foreach (var pattern in denyPatterns)
        {
            if (pattern.IsMatch(lower))
            {
                return "Error: Command blocked by safety guard (dangerous pattern detected)";
            }
        }

        if (allowPatterns != null && allowPatterns.Count > 0)
        {
            if (!allowPatterns.Any(p => p.IsMatch(lower)))
            {
                return "Error: Command blocked by safety guard (not in allowlist)";
            }
        }

        if (options.RestrictToWorkspace)
        {
            if (command.Contains("..\\") || command.Contains("../"))
            {
                return "Error: Command blocked by safety guard (path traversal detected)";
            }

            var workspacePath = Path.GetFullPath(options.WorkingDirectory ?? cwd);
            var absolutePaths = ExtractAbsolutePaths(command);

            foreach (var path in absolutePaths)
            {
                try
                {
                    var resolvedPath = Path.GetFullPath(path);
                    if (!resolvedPath.StartsWith(workspacePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return "Error: Command blocked by safety guard (path outside working directory)";
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private static List<string> ExtractAbsolutePaths(string command)
    {
        var paths = new List<string>();

        var winMatches = Regex.Matches(command, @"[A-Za-z]:\\[^\\'""]+");
        foreach (Match m in winMatches)
        {
            paths.Add(m.Value);
        }

        var posixMatches = Regex.Matches(command, @"(?:^|[\s|>])(/[^\s'"">]+)");
        foreach (Match m in posixMatches)
        {
            paths.Add(m.Groups[1].Value);
        }

        return paths;
    }

    private static string BuildResult(string output, string error, int exitCode)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(output))
            parts.Add(output);

        if (!string.IsNullOrEmpty(error))
            parts.Add($"STDERR:\n{error}");

        if (exitCode != 0)
            parts.Add($"Exit code: {exitCode}");

        return parts.Count > 0 ? string.Join("\n", parts) : "(no output)";
    }
}
