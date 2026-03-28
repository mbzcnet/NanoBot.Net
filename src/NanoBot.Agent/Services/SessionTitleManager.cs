using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;
using NanoBot.Agent.Extensions;

namespace NanoBot.Agent.Services;

/// <summary>
/// Manages automatic session title generation.
/// </summary>
public sealed class SessionTitleManager
{
    private static readonly Regex DefaultSessionTitleRegex = new(
        @"^会话 \d{2}-\d{2} \d{2}:\d{2}$",
        RegexOptions.Compiled);

    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SessionTitleManager>? _logger;
    private readonly Func<string, IChatClient?> _getChatClient;

    public SessionTitleManager(
        ISessionManager sessionManager,
        Func<string, IChatClient?> getChatClient,
        ILogger<SessionTitleManager>? logger = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _getChatClient = getChatClient ?? throw new ArgumentNullException(nameof(getChatClient));
        _logger = logger;
    }

    /// <summary>
    /// Attempts to automatically set a session title based on the user's message.
    /// </summary>
    public async Task TryAutoSetSessionTitleAsync(
        AgentSession session,
        string sessionKey,
        string userContent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var currentTitle = _sessionManager.GetSessionTitle(sessionKey);

            if (!string.IsNullOrEmpty(currentTitle) && !IsDefaultSessionTitle(currentTitle))
            {
                return;
            }

            var messages = GetSessionMessages(session);

            if (messages.Count == 0)
            {
                string newTitle;

                if (userContent.Length > 50)
                {
                    newTitle = await GenerateTitleWithLLMAsync(sessionKey, userContent, cancellationToken);

                    if (string.IsNullOrWhiteSpace(newTitle))
                    {
                        newTitle = userContent[..50] + "...";
                    }
                }
                else
                {
                    newTitle = userContent;
                }

                if (!string.IsNullOrWhiteSpace(newTitle))
                {
                    _sessionManager.SetSessionTitle(sessionKey, newTitle);
                    await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);
                    _logger?.LogInformation("Auto-set session title for {SessionKey} to: {Title}", sessionKey, newTitle);
                }
            }
            else if (messages.Count > 0 && IsDefaultSessionTitle(currentTitle))
            {
                var firstUserMessage = messages.FirstOrDefault(m => m.Role == ChatRole.User);
                if (firstUserMessage?.Text is not null)
                {
                    var firstContent = firstUserMessage.Text;
                    string newTitle;

                    if (firstContent.Length > 50)
                    {
                        newTitle = await GenerateTitleWithLLMAsync(sessionKey, firstContent, cancellationToken);
                        if (string.IsNullOrWhiteSpace(newTitle))
                        {
                            newTitle = firstContent[..50] + "...";
                        }
                    }
                    else
                    {
                        newTitle = firstContent;
                    }

                    if (!string.IsNullOrWhiteSpace(newTitle))
                    {
                        _sessionManager.SetSessionTitle(sessionKey, newTitle);
                        await _sessionManager.SaveSessionAsync(session, sessionKey, cancellationToken);
                        _logger?.LogInformation("Backfilled session title for {SessionKey} to: {Title}", sessionKey, newTitle);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to auto-set session title for {SessionKey}", sessionKey);
        }
    }

    /// <summary>
    /// Generates a short title for the session using the LLM.
    /// </summary>
    public async Task<string> GenerateTitleWithLLMAsync(
        string sessionKey,
        string userContent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var chatClient = _getChatClient(sessionKey);
            if (chatClient == null)
            {
                _logger?.LogWarning("No chat client available for session {SessionKey}", sessionKey);
                return string.Empty;
            }

            var systemPrompt = "You are a title generator. Generate a very short title (max 10 characters, Chinese OK) for the user's message. ONLY output the title, nothing else. No punctuation, no quotes, no explanation.";

            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userContent)
                ],
                cancellationToken: cancellationToken);

            var title = response.Messages.FirstOrDefault()?.Text?.Trim() ?? string.Empty;

            title = title.Trim('"', '\'', '。', '.', '！', '!', '？', '?', ' ', '\n', '\r');

            if (string.IsNullOrWhiteSpace(title) || title == userContent)
            {
                return string.Empty;
            }

            return title;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to generate title with LLM for session {SessionKey}", sessionKey);
            return string.Empty;
        }
    }

    /// <summary>
    /// Checks if the title matches the default session title format (e.g., "会话 03-16 14:30").
    /// </summary>
    public static bool IsDefaultSessionTitle(string title)
    {
        return !string.IsNullOrEmpty(title) && DefaultSessionTitleRegex.IsMatch(title);
    }

    private static List<ChatMessage> GetSessionMessages(AgentSession session)
    {
        return session.GetAllMessages().ToList();
    }
}
