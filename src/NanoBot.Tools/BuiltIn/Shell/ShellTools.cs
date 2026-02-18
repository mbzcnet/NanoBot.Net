using Microsoft.Extensions.AI;

namespace NanoBot.Tools.BuiltIn;

public static class ShellTools
{
    public static AITool CreateExecTool(IEnumerable<string>? blockedCommands = null)
    {
        var blocked = new HashSet<string>(blockedCommands ?? [], StringComparer.OrdinalIgnoreCase);
        
        return AIFunctionFactory.Create(
            (string command, int timeoutSeconds) => ExecuteAsync(command, timeoutSeconds, blocked),
            new AIFunctionFactoryOptions
            {
                Name = "exec",
                Description = "Execute a shell command and return the output. Use with caution as this can be dangerous."
            });
    }

    private static async Task<string> ExecuteAsync(string command, int timeoutSeconds, HashSet<string> blockedCommands)
    {
        try
        {
            var parts = command.Split(' ', 2);
            var cmdName = parts[0];

            if (blockedCommands.Contains(cmdName))
            {
                return $"Error: Command '{cmdName}' is blocked for security reasons.";
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 30));

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "/bin/sh",
                    Arguments = Environment.OSVersion.Platform == PlatformID.Win32NT 
                        ? $"/c {command}" 
                        : $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                return $"Exit code: {process.ExitCode}\nOutput: {output}\nError: {error}";
            }

            return string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";
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
}
