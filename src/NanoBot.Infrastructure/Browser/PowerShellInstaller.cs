using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Tools.Browser;

namespace NanoBot.Infrastructure.Browser;

/// <summary>
/// Implements PowerShell Core (pwsh) installation using platform-appropriate methods.
/// </summary>
public class PowerShellInstaller : IPowerShellInstaller
{
    private readonly ILogger<PowerShellInstaller>? _logger;
    private string? _lastError;

    public PowerShellInstaller(ILogger<PowerShellInstaller>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        var path = await GetPowerShellPathAsync(cancellationToken);
        return !string.IsNullOrEmpty(path);
    }

    /// <inheritdoc />
    public async Task<string?> GetPowerShellPathAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to find pwsh in PATH
            var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pwsh.exe" : "pwsh";

            // First check if pwsh is directly available
            if (await CommandExistsAsync(fileName, cancellationToken))
            {
                return fileName;
            }

            // On Windows, also check common installation paths
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                var possiblePaths = new[]
                {
                    Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"),
                    Path.Combine(localAppData, "Microsoft", "PowerShell", "pwsh.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Microsoft", "PowerShell", "pwsh.exe")
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            // On Linux/macOS, check common paths
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var possiblePaths = new[]
                {
                    "/usr/bin/pwsh",
                    "/usr/local/bin/pwsh",
                    "/opt/microsoft/powershell/7/pwsh",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".powershell", "pwsh")
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error checking for PowerShell installation");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> InstallAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("Starting PowerShell Core installation...");

            // Check if already installed
            if (await IsInstalledAsync(cancellationToken))
            {
                _logger?.LogInformation("PowerShell Core is already installed");
                return true;
            }

            // Platform-specific installation
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await InstallOnWindowsAsync(cancellationToken);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return await InstallOnLinuxAsync(cancellationToken);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await InstallOnMacOSAsync(cancellationToken);
            }
            else
            {
                _lastError = $"Unsupported operating system: {RuntimeInformation.OSDescription}";
                _logger?.LogError(_lastError);
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("PowerShell installation was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to install PowerShell: {ex.Message}";
            _logger?.LogError(ex, _lastError);
            return false;
        }
    }

    /// <inheritdoc />
    public string GetStatusMessage()
    {
        if (_lastError != null)
        {
            return $"PowerShell not installed. Last error: {_lastError}";
        }
        return "PowerShell status unknown";
    }

    /// <summary>
    /// Installs PowerShell on Windows using winget or direct download.
    /// </summary>
    private async Task<bool> InstallOnWindowsAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Installing PowerShell on Windows...");

        // Try winget first (Windows Package Manager)
        if (await CommandExistsAsync("winget", cancellationToken))
        {
            _logger?.LogInformation("Attempting installation via winget...");
            var (exitCode, error) = await RunProcessAsync("winget", new[] { "install", "--id", "Microsoft.PowerShell", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements" }, cancellationToken);

            if (exitCode == 0)
            {
                _logger?.LogInformation("PowerShell installed successfully via winget");
                return true;
            }

            _logger?.LogDebug("winget installation failed: {Error}", error);
        }

        // Try using Invoke-Expression (irm | iex) method
        _logger?.LogInformation("Attempting installation via PowerShell bootstrap script...");
        var (irmExitCode, irmError) = await RunProcessAsync("powershell.exe", new[] { "-Command", "Invoke-Expression", "& { Invoke-RestMethod https://aka.ms/install-powershell.ps1 | Invoke-Expression }" }, cancellationToken);

        if (irmExitCode == 0 || await IsInstalledAsync(cancellationToken))
        {
            _logger?.LogInformation("PowerShell installed successfully via bootstrap script");
            return true;
        }

        _logger?.LogDebug("Bootstrap script installation failed: {Error}", irmError);

        _lastError = "Failed to install PowerShell on Windows. Please install manually from https://aka.ms/powershell";
        return false;
    }

    /// <summary>
    /// Installs PowerShell on Linux using distribution-specific package managers.
    /// </summary>
    private async Task<bool> InstallOnLinuxAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Installing PowerShell on Linux...");

        // Detect distribution
        var distro = await DetectLinuxDistributionAsync(cancellationToken);
        _logger?.LogInformation("Detected Linux distribution: {Distro}", distro);

        switch (distro.ToLowerInvariant())
        {
            case "ubuntu":
            case "debian":
                return await InstallOnDebianAsync(cancellationToken);
            case "fedora":
            case "rhel":
            case "centos":
            case "rocky":
            case "almalinux":
                return await InstallOnRedHatAsync(cancellationToken);
            case "arch":
            case "manjaro":
                return await InstallOnArchAsync(cancellationToken);
            case "alpine":
                return await InstallOnAlpineAsync(cancellationToken);
            default:
                // Try generic installation via snap or direct download
                if (await CommandExistsAsync("snap", cancellationToken))
                {
                    return await InstallViaSnapAsync(cancellationToken);
                }
                return await InstallViaDirectDownloadLinuxAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Installs PowerShell on Debian/Ubuntu using apt.
    /// </summary>
    private async Task<bool> InstallOnDebianAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Installing PowerShell on Debian/Ubuntu...");

        // Update package list and install prerequisites
        var prerequisites = new[] { "apt-get", "update" };
        await RunProcessAsync("sudo", prerequisites, cancellationToken);

        // Install prerequisites
        var prereqArgs = new[] { "apt-get", "install", "-y", "wget", "apt-transport-https", "software-properties-common" };
        await RunProcessAsync("sudo", prereqArgs, cancellationToken);

        // Download and install Microsoft repository GPG keys
        var (gpgExit, gpgError) = await RunProcessAsync("wget", new[] { "-q", "https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb", "-O", "/tmp/packages-microsoft-prod.deb" }, cancellationToken);

        if (gpgExit == 0)
        {
            await RunProcessAsync("sudo", new[] { "dpkg", "-i", "/tmp/packages-microsoft-prod.deb" }, cancellationToken);
            await RunProcessAsync("sudo", new[] { "apt-get", "update" }, cancellationToken);
        }

        // Install PowerShell
        var (exitCode, error) = await RunProcessAsync("sudo", new[] { "apt-get", "install", "-y", "powershell" }, cancellationToken);

        if (exitCode == 0 || await IsInstalledAsync(cancellationToken))
        {
            _logger?.LogInformation("PowerShell installed successfully on Debian/Ubuntu");
            return true;
        }

        // Fallback to snap
        if (await CommandExistsAsync("snap", cancellationToken))
        {
            return await InstallViaSnapAsync(cancellationToken);
        }

        _lastError = $"Failed to install PowerShell on Debian/Ubuntu: {error}";
        return false;
    }

    /// <summary>
    /// Installs PowerShell on Red Hat/Fedora/CentOS using dnf/yum.
    /// </summary>
    private async Task<bool> InstallOnRedHatAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Installing PowerShell on Red Hat family...");

        // Register Microsoft repository
        var repoCmd = "curl https://packages.microsoft.com/config/rhel/8/prod.repo | sudo tee /etc/yum.repos.d/microsoft.repo";
        await RunProcessAsync("bash", new[] { "-c", repoCmd }, cancellationToken);

        // Try dnf first, then yum
        string[] installCmd;
        if (await CommandExistsAsync("dnf", cancellationToken))
        {
            installCmd = new[] { "dnf", "install", "-y", "powershell" };
        }
        else
        {
            installCmd = new[] { "yum", "install", "-y", "powershell" };
        }

        var (exitCode, error) = await RunProcessAsync("sudo", installCmd, cancellationToken);

        if (exitCode == 0 || await IsInstalledAsync(cancellationToken))
        {
            _logger?.LogInformation("PowerShell installed successfully on Red Hat family");
            return true;
        }

        // Fallback to snap
        if (await CommandExistsAsync("snap", cancellationToken))
        {
            return await InstallViaSnapAsync(cancellationToken);
        }

        _lastError = $"Failed to install PowerShell on Red Hat family: {error}";
        return false;
    }

    /// <summary>
    /// Installs PowerShell on Arch Linux using pacman.
    /// </summary>
    private async Task<bool> InstallOnArchAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Installing PowerShell on Arch Linux...");

        var (exitCode, error) = await RunProcessAsync("sudo", new[] { "pacman", "-S", "--noconfirm", "powershell" }, cancellationToken);

        if (exitCode == 0 || await IsInstalledAsync(cancellationToken))
        {
            _logger?.LogInformation("PowerShell installed successfully on Arch Linux");
            return true;
        }

        // Try AUR via yay or paru
        if (await CommandExistsAsync("yay", cancellationToken))
        {
            var (yayExit, _) = await RunProcessAsync("yay", new[] { "-S", "--noconfirm", "powershell-bin" }, cancellationToken);
            if (yayExit == 0 || await IsInstalledAsync(cancellationToken))
            {
                return true;
            }
        }

        if (await CommandExistsAsync("paru", cancellationToken))
        {
            var (paruExit, _) = await RunProcessAsync("paru", new[] { "-S", "--noconfirm", "powershell-bin" }, cancellationToken);
            if (paruExit == 0 || await IsInstalledAsync(cancellationToken))
            {
                return true;
            }
        }

        _lastError = $"Failed to install PowerShell on Arch Linux: {error}";
        return false;
    }

    /// <summary>
    /// Installs PowerShell on Alpine Linux using apk.
    /// </summary>
    private async Task<bool> InstallOnAlpineAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Installing PowerShell on Alpine Linux...");

        // Install prerequisites
        await RunProcessAsync("apk", new[] { "add", "--no-cache", "ca-certificates", "less", "ncurses-terminfo-base", "krb5-libs", "libgcc", "libintl", "libssl3", "libstdc++", "tzdata", "userspace-rcu", "zlib", "icu-libs", "curl" }, cancellationToken);

        // Download and install PowerShell
        var version = "7.4.2"; // Use a known stable version
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var downloadUrl = $"https://github.com/PowerShell/PowerShell/releases/download/v{version}/powershell-{version}-linux-musl-{arch}.tar.gz";

        var (exitCode, error) = await RunProcessAsync("bash", new[] { "-c", $"mkdir -p /opt/microsoft/powershell/7 && curl -L {downloadUrl} | tar zx -C /opt/microsoft/powershell/7 && chmod +x /opt/microsoft/powershell/7/pwsh && ln -s /opt/microsoft/powershell/7/pwsh /usr/bin/pwsh" }, cancellationToken);

        if (exitCode == 0 || await IsInstalledAsync(cancellationToken))
        {
            _logger?.LogInformation("PowerShell installed successfully on Alpine Linux");
            return true;
        }

        _lastError = $"Failed to install PowerShell on Alpine Linux: {error}";
        return false;
    }

    /// <summary>
    /// Installs PowerShell using snap.
    /// </summary>
    private async Task<bool> InstallViaSnapAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Installing PowerShell via snap...");

        var (exitCode, error) = await RunProcessAsync("sudo", new[] { "snap", "install", "powershell", "--classic" }, cancellationToken);

        if (exitCode == 0 || await IsInstalledAsync(cancellationToken))
        {
            _logger?.LogInformation("PowerShell installed successfully via snap");
            return true;
        }

        _lastError = $"Failed to install PowerShell via snap: {error}";
        return false;
    }

    /// <summary>
    /// Installs PowerShell on macOS using Homebrew.
    /// </summary>
    private async Task<bool> InstallOnMacOSAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Installing PowerShell on macOS...");

        // Check for Homebrew
        if (await CommandExistsAsync("brew", cancellationToken))
        {
            _logger?.LogInformation("Installing via Homebrew...");
            var (exitCode, error) = await RunProcessAsync("brew", new[] { "install", "--cask", "powershell" }, cancellationToken);

            if (exitCode == 0 || await IsInstalledAsync(cancellationToken))
            {
                _logger?.LogInformation("PowerShell installed successfully via Homebrew");
                return true;
            }

            _logger?.LogDebug("Homebrew installation failed: {Error}", error);
        }

        // Fallback to direct download
        return await InstallViaDirectDownloadMacOSAsync(cancellationToken);
    }

    /// <summary>
    /// Installs PowerShell on Linux via direct download.
    /// </summary>
    private async Task<bool> InstallViaDirectDownloadLinuxAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Installing PowerShell via direct download (Linux)...");

        var version = "7.4.2"; // Use a known stable version
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

        string downloadUrl;
        if (await DetectLinuxDistributionAsync(cancellationToken) == "alpine")
        {
            downloadUrl = $"https://github.com/PowerShell/PowerShell/releases/download/v{version}/powershell-{version}-linux-musl-{arch}.tar.gz";
        }
        else
        {
            downloadUrl = $"https://github.com/PowerShell/PowerShell/releases/download/v{version}/powershell-{version}-linux-{arch}.tar.gz";
        }

        var installDir = "/opt/microsoft/powershell/7";
        var (exitCode, error) = await RunProcessAsync("bash", new[] { "-c", $"mkdir -p {installDir} && curl -L {downloadUrl} | tar zx -C {installDir} && chmod +x {installDir}/pwsh && ln -sf {installDir}/pwsh /usr/bin/pwsh" }, cancellationToken);

        if (exitCode == 0 || await IsInstalledAsync(cancellationToken))
        {
            _logger?.LogInformation("PowerShell installed successfully via direct download");
            return true;
        }

        _lastError = $"Failed to install PowerShell via direct download: {error}";
        return false;
    }

    /// <summary>
    /// Installs PowerShell on macOS via direct download.
    /// </summary>
    private async Task<bool> InstallViaDirectDownloadMacOSAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Installing PowerShell via direct download (macOS)...");

        var version = "7.4.2"; // Use a known stable version
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var downloadUrl = $"https://github.com/PowerShell/PowerShell/releases/download/v{version}/powershell-{version}-osx-{arch}.pkg";

        var pkgPath = "/tmp/powershell.pkg";
        var (curlExit, curlError) = await RunProcessAsync("curl", new[] { "-L", "-o", pkgPath, downloadUrl }, cancellationToken);

        if (curlExit != 0)
        {
            _lastError = $"Failed to download PowerShell package: {curlError}";
            return false;
        }

        var (exitCode, error) = await RunProcessAsync("sudo", new[] { "installer", "-pkg", pkgPath, "-target", "/" }, cancellationToken);

        if (exitCode == 0 || await IsInstalledAsync(cancellationToken))
        {
            _logger?.LogInformation("PowerShell installed successfully via direct download");
            return true;
        }

        _lastError = $"Failed to install PowerShell package: {error}";
        return false;
    }

    /// <summary>
    /// Detects the Linux distribution.
    /// </summary>
    private async Task<string> DetectLinuxDistributionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Try /etc/os-release first
            if (File.Exists("/etc/os-release"))
            {
                var content = await File.ReadAllTextAsync("/etc/os-release", cancellationToken);
                var lines = content.Split('\n');
                var idLine = lines.FirstOrDefault(l => l.StartsWith("ID="));
                if (idLine != null)
                {
                    return idLine.Substring(3).Trim('"').ToLowerInvariant();
                }
            }

            // Fallback to lsb_release
            var (exitCode, output) = await RunProcessAsync("lsb_release", new[] { "-is" }, cancellationToken);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return output.Trim().ToLowerInvariant();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error detecting Linux distribution");
        }

        return "unknown";
    }

    /// <summary>
    /// Checks if a command exists in PATH.
    /// </summary>
    private async Task<bool> CommandExistsAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
            var (exitCode, _) = await RunProcessAsync(fileName, new[] { command }, cancellationToken);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Runs a process with the specified arguments.
    /// </summary>
    private async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string[] args, CancellationToken cancellationToken)
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

            var combinedOutput = !string.IsNullOrWhiteSpace(output) ? output : error;
            return (process.ExitCode, combinedOutput.Trim());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { /* Ignore */ }
            throw;
        }
    }
}
