using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Infrastructure.Browser;

/// <summary>
/// Implements Playwright browser installation using multiple fallback methods.
/// </summary>
public class PlaywrightInstaller : IPlaywrightInstaller
{
    private readonly ILogger<PlaywrightInstaller>? _logger;
    private readonly IPowerShellInstaller? _powerShellInstaller;
    private string? _lastError;

    public PlaywrightInstaller(ILogger<PlaywrightInstaller>? logger = null, IPowerShellInstaller? powerShellInstaller = null)
    {
        _logger = logger;
        _powerShellInstaller = powerShellInstaller;
    }

    /// <inheritdoc />
    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Redirect console output to capture platform errors
            var originalOut = Console.Out;
            var originalError = Console.Error;
            var outputWriter = new StringWriter();
            var errorWriter = new StringWriter();

            Console.SetOut(outputWriter);
            Console.SetError(errorWriter);

            try
            {
                // Try to create a Playwright instance and launch a browser
                var playwright = await Playwright.CreateAsync();
                try
                {
                    // Try to launch Chromium in headless mode with a short timeout
                    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = true,
                        Timeout = 5000
                    });
                    return true;
                }
                finally
                {
                    playwright.Dispose();
                }
            }
            finally
            {
                // Restore console output
                Console.SetOut(originalOut);
                Console.SetError(originalError);

                // Check captured output for platform errors
                var capturedOutput = outputWriter.ToString() + errorWriter.ToString();
                if (!string.IsNullOrWhiteSpace(capturedOutput) && capturedOutput.Contains("does not support"))
                {
                    _lastError = capturedOutput.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Playwright browsers not installed or not accessible");
            // Don't overwrite platform errors - they are more specific and useful
            if (_lastError == null || (!_lastError.Contains("does not support") && !_lastError.Contains("unsupported") && !_lastError.Contains("operating system may not be supported")))
            {
                _lastError = ex.Message;
            }
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> InstallAsync(string[]? browsers = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("Starting Playwright browser installation...");

            // Check if already installed
            if (await IsInstalledAsync(cancellationToken))
            {
                _logger?.LogInformation("Playwright browsers already installed");
                return true;
            }

            // Default to chromium if not specified
            var browserList = browsers?.Length > 0 ? browsers : new[] { "chromium" };

            // Try multiple installation methods
            var methods = new[]
            {
                ("Reflection", (Func<string[], CancellationToken, Task<(int ExitCode, string Error)>>)RunViaReflectionAsync),
                ("CLI tool", (Func<string[], CancellationToken, Task<(int ExitCode, string Error)>>)RunViaCliToolAsync),
                ("Global CLI", (Func<string[], CancellationToken, Task<(int ExitCode, string Error)>>)RunViaGlobalCliAsync),
                ("PowerShell", (Func<string[], CancellationToken, Task<(int ExitCode, string Error)>>)RunViaPowerShellAsync)
            };

            foreach (var (methodName, methodFunc) in methods)
            {
                try
                {
                    _logger?.LogDebug("Trying Playwright installation via {Method}", methodName);
                    var (exitCode, error) = await methodFunc(browserList, cancellationToken);

                    // Check for platform error and preserve it
                    if (!string.IsNullOrEmpty(error) && (error.Contains("does not support") || error.Contains("unsupported") || error.Contains("operating system may not be supported")))
                    {
                        _lastError = error;
                        _logger?.LogDebug("Platform not supported error detected via {Method}: {Error}", methodName, error);
                        // Platform errors are fatal - no point trying other methods
                        break;
                    }

                    if (exitCode == 0)
                    {
                        // Before verifying, check if we already have a platform error
                        // If so, don't bother verifying - the platform is not supported
                        if (!string.IsNullOrEmpty(_lastError) &&
                            (_lastError.Contains("does not support") || _lastError.Contains("unsupported") || _lastError.Contains("operating system may not be supported")))
                        {
                            _logger?.LogError("Platform not supported, skipping verification");
                            return false;
                        }

                        // Verify installation worked
                        if (await IsInstalledAsync(cancellationToken))
                        {
                            _logger?.LogInformation("Playwright browsers installed successfully via {Method}", methodName);
                            return true;
                        }
                    }
                    else if (!string.IsNullOrEmpty(error))
                    {
                        // Only set _lastError if we don't already have a platform error
                        if (_lastError == null || (!_lastError.Contains("does not support") && !_lastError.Contains("unsupported") && !_lastError.Contains("operating system may not be supported")))
                        {
                            _lastError = error;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Playwright installation via {Method} failed", methodName);
                    // Continue to next method
                }
            }

            _logger?.LogError("All Playwright installation methods failed");
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Playwright browser installation was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to install Playwright browsers");
            _lastError = ex.Message;
            return false;
        }
    }

    /// <inheritdoc />
    public string GetStatusMessage()
    {
        if (_lastError != null)
        {
            return $"Playwright browsers not installed. Last error: {_lastError}";
        }
        return "Playwright browsers status unknown";
    }

    /// <summary>
    /// Try to run the installer via reflection on Microsoft.Playwright.Program.Main
    /// </summary>
    private async Task<(int ExitCode, string Error)> RunViaReflectionAsync(string[] browsers, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var programType = typeof(Playwright).Assembly.GetType("Microsoft.Playwright.Program");
            if (programType == null)
            {
                _logger?.LogDebug("Microsoft.Playwright.Program type not found");
                return (-1, "Microsoft.Playwright.Program type not found");
            }

            var mainMethod = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string[]) });
            if (mainMethod == null)
            {
                _logger?.LogDebug("Microsoft.Playwright.Program.Main method not found");
                return (-1, "Microsoft.Playwright.Program.Main method not found");
            }

            var args = new List<string> { "install" };
            args.AddRange(browsers);

            // Redirect console output to capture error messages
            var originalOut = Console.Out;
            var originalError = Console.Error;
            var stringWriter = new StringWriter();
            var stringErrorWriter = new StringWriter();

            try
            {
                Console.SetOut(stringWriter);
                Console.SetError(stringErrorWriter);

                var result = mainMethod.Invoke(null, new object[] { args.ToArray() });
                var exitCode = result as int? ?? -1;

                // Restore console output
                Console.SetOut(originalOut);
                Console.SetError(originalError);

                var output = stringWriter.ToString();
                var error = stringErrorWriter.ToString();

                // Combine output and error, preferring error if it contains error messages
                var combinedError = !string.IsNullOrWhiteSpace(error) ? error : output;

                // Playwright writes platform errors directly to console, bypassing our redirection
                // If exit code is 1 and we have no captured error, check if _lastError contains platform info
                // or use a generic platform error message
                if (exitCode != 0 && string.IsNullOrWhiteSpace(combinedError))
                {
                    combinedError = "Failed to install browsers. The operating system may not be supported.";
                }

                return (exitCode, combinedError.Trim());
            }
            catch (Exception ex)
            {
                // Restore console output on exception
                Console.SetOut(originalOut);
                Console.SetError(originalError);

                _logger?.LogDebug(ex, "Failed to invoke Playwright.Program.Main via reflection");
                return (-1, ex.Message);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Try to run the installer via dotnet tool run playwright
    /// </summary>
    private async Task<(int ExitCode, string Error)> RunViaCliToolAsync(string[] browsers, CancellationToken cancellationToken)
    {
        var args = new List<string> { "tool", "run", "playwright", "install" };
        args.AddRange(browsers);
        return await RunProcessAsync("dotnet", args, cancellationToken);
    }

    /// <summary>
    /// Try to run the installer via global playwright command
    /// </summary>
    private async Task<(int ExitCode, string Error)> RunViaGlobalCliAsync(string[] browsers, CancellationToken cancellationToken)
    {
        var args = new List<string> { "install" };
        args.AddRange(browsers);
        return await RunProcessAsync("playwright", args, cancellationToken);
    }

    /// <summary>
    /// Try to run the installer via PowerShell script
    /// </summary>
    private async Task<(int ExitCode, string Error)> RunViaPowerShellAsync(string[] browsers, CancellationToken cancellationToken)
    {
        // Check if PowerShell is installed, and try to install it if not
        string? pwshPath = null;
        if (_powerShellInstaller != null)
        {
            pwshPath = await _powerShellInstaller.GetPowerShellPathAsync(cancellationToken);
            if (string.IsNullOrEmpty(pwshPath))
            {
                _logger?.LogInformation("PowerShell not found, attempting to install...");
                var installed = await _powerShellInstaller.InstallAsync(cancellationToken);
                if (installed)
                {
                    pwshPath = await _powerShellInstaller.GetPowerShellPathAsync(cancellationToken);
                    _logger?.LogInformation("PowerShell installed successfully at {Path}", pwshPath);
                }
                else
                {
                    var errorMsg = _powerShellInstaller.GetStatusMessage();
                    _logger?.LogWarning("Failed to install PowerShell: {Error}", errorMsg);
                    return (-1, $"PowerShell is not installed and could not be installed automatically. {errorMsg}");
                }
            }
        }
        else
        {
            // Fallback: just try "pwsh" command
            pwshPath = "pwsh";
        }

        // Find the playwright.ps1 script location
        var playbookwrightDir = typeof(Playwright).Assembly.Location;
        var playbookwrightRoot = Path.GetDirectoryName(playbookwrightDir);
        var psScriptPath = Path.Combine(playbookwrightRoot ?? "", "playwright.ps1");

        if (!File.Exists(psScriptPath))
        {
            _logger?.LogDebug("playwright.ps1 not found at {Path}", psScriptPath);
            return (-1, $"playwright.ps1 not found at {psScriptPath}");
        }

        var args = new List<string> { "-ExecutionPolicy", "Bypass", "-File", psScriptPath, "install" };
        args.AddRange(browsers);
        return await RunProcessAsync(pwshPath ?? "pwsh", args, cancellationToken);
    }

    /// <summary>
    /// Run a process with the specified arguments
    /// </summary>
    private async Task<(int ExitCode, string Error)> RunProcessAsync(string fileName, List<string> args, CancellationToken cancellationToken)
    {
        var arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        _logger?.LogDebug("Running: {FileName} {Arguments}", fileName, arguments);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
            var waitTask = process.WaitForExitAsync(cts.Token);

            await Task.WhenAll(outputTask, errorTask, waitTask);

            var output = await outputTask;
            var error = await errorTask;

            if (!string.IsNullOrWhiteSpace(output))
            {
                _logger?.LogDebug("Playwright install output: {Output}", output);
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger?.LogDebug("Playwright install error: {Error}", error);
            }

            // Check for platform not supported in error output
            if (!string.IsNullOrEmpty(error) && error.Contains("does not support"))
            {
                return (process.ExitCode, error.Trim());
            }

            return (process.ExitCode, error.Trim());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { /* Ignore */ }
            throw;
        }
    }
}
