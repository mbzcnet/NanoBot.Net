namespace NanoBot.Core.Tools.Rpa;

/// <summary>
/// Executes RPA flows and actions.
/// </summary>
public interface IRpaExecutor
{
    /// <summary>
    /// Executes a complete RPA flow.
    /// </summary>
    Task<RpaFlowResult> ExecuteFlowAsync(RpaFlowRequest request, CancellationToken ct = default);

    /// <summary>
    /// Executes a single RPA action.
    /// </summary>
    Task ExecuteActionAsync(RpaAction action, CancellationToken ct = default);
}
