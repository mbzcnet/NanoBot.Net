namespace NanoBot.Core.Tools.Rpa;

/// <summary>
/// Analyzes screen content using OmniParser.
/// </summary>
public interface IScreenAnalyzer
{
    /// <summary>
    /// Analyzes the primary screen using OmniParser.
    /// </summary>
    Task<OmniParserResult> AnalyzeScreenAsync(CancellationToken ct = default);
}
