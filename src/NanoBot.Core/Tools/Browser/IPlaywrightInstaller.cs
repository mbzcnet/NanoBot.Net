namespace NanoBot.Core.Tools.Browser;

/// <summary>
/// Service for installing Playwright browser binaries.
/// </summary>
public interface IPlaywrightInstaller
{
    /// <summary>
    /// Checks if Playwright browsers are installed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if browsers are installed, false otherwise.</returns>
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs Playwright browsers.
    /// </summary>
    /// <param name="browsers">Optional specific browsers to install (e.g., "chromium"). If null, installs all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if installation succeeded, false otherwise.</returns>
    Task<bool> InstallAsync(string[]? browsers = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the installation status message.
    /// </summary>
    /// <returns>A message describing the installation status.</returns>
    string GetStatusMessage();
}
