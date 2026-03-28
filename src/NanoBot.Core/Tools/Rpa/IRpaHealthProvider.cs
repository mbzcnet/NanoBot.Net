namespace NanoBot.Core.Tools.Rpa;

/// <summary>
/// Provides RPA health status information.
/// </summary>
public interface IRpaHealthProvider
{
    /// <summary>
    /// Gets the current health status of RPA services.
    /// </summary>
    Task<RpaHealthStatus> GetHealthStatusAsync(CancellationToken ct = default);
}
