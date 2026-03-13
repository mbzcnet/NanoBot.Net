namespace NanoBot.Core.Tools.Browser;

/// <summary>
/// Service for installing PowerShell Core (pwsh) on various platforms.
/// </summary>
public interface IPowerShellInstaller
{
    /// <summary>
    /// Checks if PowerShell Core (pwsh) is installed and available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if PowerShell is installed, false otherwise.</returns>
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the path to the PowerShell executable (pwsh).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path to the pwsh executable, or null if not found.</returns>
    Task<string?> GetPowerShellPathAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs PowerShell Core using platform-appropriate methods.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if installation succeeded, false otherwise.</returns>
    Task<bool> InstallAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the installation status message.
    /// </summary>
    /// <returns>A message describing the installation status.</returns>
    string GetStatusMessage();
}
