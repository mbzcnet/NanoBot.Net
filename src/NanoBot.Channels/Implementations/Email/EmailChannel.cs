using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using MimeKit;
using NanoBot.Channels.Abstractions;
using NanoBot.Core.Bus;
using NanoBot.Core.Configuration;

namespace NanoBot.Channels.Implementations.Email;

public partial class EmailChannel : ChannelBase
{
    public override string Id => "email";
    public override string Type => "email";

    private readonly EmailConfig _config;
    private readonly ConcurrentDictionary<string, string> _lastSubjectByChat = new();
    private readonly ConcurrentDictionary<string, string> _lastMessageIdByChat = new();
    private readonly ConcurrentDictionary<string, byte> _processedUids = new();
    private const int MaxProcessedUids = 100000;

    public EmailChannel(EmailConfig config, IMessageBus bus, ILogger<EmailChannel> logger)
        : base(bus, logger)
    {
        _config = config;
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.ConsentGranted)
        {
            _logger.LogWarning("Email channel disabled: consent_granted is false");
            return;
        }

        if (!ValidateConfig())
            return;

        _running = true;
        _logger.LogInformation("Starting Email channel (IMAP polling mode)...");

        var pollSeconds = Math.Max(5, _config.PollIntervalSeconds);

        while (_running && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var messages = await FetchNewMessagesAsync(cancellationToken);

                foreach (var msg in messages)
                {
                    var sender = msg.Sender;
                    if (!string.IsNullOrEmpty(msg.Subject))
                        _lastSubjectByChat[sender] = msg.Subject;
                    if (!string.IsNullOrEmpty(msg.MessageId))
                        _lastMessageIdByChat[sender] = msg.MessageId;

                    await HandleMessageAsync(
                        sender,
                        sender,
                        msg.Content,
                        Array.Empty<string>(),
                        msg.Metadata
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email polling error");
            }

            await Task.Delay(pollSeconds * 1000, cancellationToken);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        _running = false;
        _logger.LogInformation("Email channel stopped");
        return Task.CompletedTask;
    }

    public override async Task SendMessageAsync(OutboundMessage message, CancellationToken cancellationToken = default)
    {
        if (!_config.ConsentGranted)
        {
            _logger.LogWarning("Skip email send: consent_granted is false");
            return;
        }

        var forceSend = message.Metadata?.TryGetValue("force_send", out var force) == true && force is true;
        if (!_config.AutoReplyEnabled && !forceSend)
        {
            _logger.LogInformation("Skip automatic email reply: auto_reply_enabled is false");
            return;
        }

        if (string.IsNullOrEmpty(_config.SmtpHost))
        {
            _logger.LogWarning("Email channel SMTP host not configured");
            return;
        }

        var toAddr = message.ChatId.Trim();
        if (string.IsNullOrEmpty(toAddr))
        {
            _logger.LogWarning("Email channel missing recipient address");
            return;
        }

        var baseSubject = _lastSubjectByChat.GetValueOrDefault(toAddr, "nanobot reply");
        var subject = ReplySubject(baseSubject);

        if (message.Metadata?.TryGetValue("subject", out var overrideSubject) == true && overrideSubject is string s && !string.IsNullOrEmpty(s))
        {
            subject = s.Trim();
        }

        var emailMessage = new MimeMessage();
        emailMessage.From.Add(new MailboxAddress("", _config.FromAddress ?? _config.SmtpUsername ?? _config.ImapUsername));
        emailMessage.To.Add(new MailboxAddress("", toAddr));
        emailMessage.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            TextBody = message.Content ?? ""
        };
        emailMessage.Body = bodyBuilder.ToMessageBody();

        if (_lastMessageIdByChat.TryGetValue(toAddr, out var inReplyTo) && !string.IsNullOrEmpty(inReplyTo))
        {
            emailMessage.InReplyTo = inReplyTo;
            emailMessage.References.Add(inReplyTo);
        }

        try
        {
            await SmtpSendAsync(emailMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email to {To}", toAddr);
            throw;
        }
    }

    private bool ValidateConfig()
    {
        var missing = new List<string>();

        if (string.IsNullOrEmpty(_config.ImapHost)) missing.Add("imap_host");
        if (string.IsNullOrEmpty(_config.ImapUsername)) missing.Add("imap_username");
        if (string.IsNullOrEmpty(_config.ImapPassword)) missing.Add("imap_password");
        if (string.IsNullOrEmpty(_config.SmtpHost)) missing.Add("smtp_host");
        if (string.IsNullOrEmpty(_config.SmtpUsername)) missing.Add("smtp_username");
        if (string.IsNullOrEmpty(_config.SmtpPassword)) missing.Add("smtp_password");

        if (missing.Count > 0)
        {
            _logger.LogError("Email channel not configured, missing: {Missing}", string.Join(", ", missing));
            return false;
        }

        return true;
    }

    private async Task SmtpSendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient();

        if (_config.SmtpUseTls)
        {
            await client.ConnectAsync(_config.SmtpHost, _config.SmtpPort, MailKit.Security.SecureSocketOptions.StartTls, cancellationToken);
        }
        else
        {
            await client.ConnectAsync(_config.SmtpHost, _config.SmtpPort, MailKit.Security.SecureSocketOptions.None, cancellationToken);
        }

        await client.AuthenticateAsync(_config.SmtpUsername, _config.SmtpPassword, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private async Task<List<EmailMessage>> FetchNewMessagesAsync(CancellationToken cancellationToken)
    {
        var messages = new List<EmailMessage>();

        using var client = new ImapClient();
        await client.ConnectAsync(_config.ImapHost, _config.ImapPort, _config.ImapUseSsl, cancellationToken);
        await client.AuthenticateAsync(_config.ImapUsername, _config.ImapPassword, cancellationToken);

        var mailbox = await client.GetFolderAsync(_config.ImapMailbox ?? "INBOX", cancellationToken);
        await mailbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        var uids = await mailbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);

        foreach (var uid in uids)
        {
            if (_processedUids.ContainsKey(uid.ToString()))
                continue;

            var message = await mailbox.GetMessageAsync(uid, cancellationToken);
            var sender = message.From.Mailboxes.FirstOrDefault()?.Address?.ToLowerInvariant();

            if (string.IsNullOrEmpty(sender))
                continue;

            var subject = message.Subject ?? "";
            var date = message.Date;
            var messageId = message.MessageId ?? "";
            var body = ExtractTextBody(message);

            if (string.IsNullOrEmpty(body))
                body = "(empty email body)";

            body = body.Length > _config.MaxBodyChars ? body[.._config.MaxBodyChars] : body;

            var content = $"Email received.\nFrom: {sender}\nSubject: {subject}\nDate: {date}\n\n{body}";

            var metadata = new Dictionary<string, object>
            {
                ["message_id"] = messageId,
                ["subject"] = subject,
                ["date"] = date.ToString(),
                ["sender_email"] = sender,
                ["uid"] = uid.ToString()
            };

            messages.Add(new EmailMessage
            {
                Sender = sender,
                Subject = subject,
                MessageId = messageId,
                Content = content,
                Metadata = metadata
            });

            _processedUids[uid.ToString()] = 0;

            if (_processedUids.Count > MaxProcessedUids)
            {
                var keysToRemove = _processedUids.Keys.Take(_processedUids.Count - MaxProcessedUids / 2);
                foreach (var key in keysToRemove)
                {
                    _processedUids.TryRemove(key, out _);
                }
            }

            if (_config.MarkSeen)
            {
                await mailbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);
            }
        }

        await client.DisconnectAsync(true, cancellationToken);
        return messages;
    }

    private static string ExtractTextBody(MimeMessage message)
    {
        if (message.Body is TextPart textPart)
        {
            return textPart.Text ?? "";
        }

        if (message.Body is Multipart multipart)
        {
            var plainParts = new List<string>();
            var htmlParts = new List<string>();

            foreach (var part in multipart)
            {
                if (part is TextPart tp)
                {
                    if (tp.ContentType.MimeType == "text/plain")
                    {
                        plainParts.Add(tp.Text ?? "");
                    }
                    else if (tp.ContentType.MimeType == "text/html")
                    {
                        htmlParts.Add(tp.Text ?? "");
                    }
                }
            }

            if (plainParts.Count > 0)
                return string.Join("\n\n", plainParts);

            if (htmlParts.Count > 0)
                return HtmlToText(string.Join("\n\n", htmlParts));
        }

        return "";
    }

    [GeneratedRegex(@"<\s*br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrRegex();

    [GeneratedRegex(@"<\s*/\s*p\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex EndPRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    private static string HtmlToText(string html)
    {
        var text = BrRegex().Replace(html, "\n");
        text = EndPRegex().Replace(text, "\n");
        text = HtmlTagRegex().Replace(text, "");
        return System.Net.WebUtility.HtmlDecode(text);
    }

    private string ReplySubject(string baseSubject)
    {
        var subject = string.IsNullOrEmpty(baseSubject) ? "nanobot reply" : baseSubject.Trim();
        if (subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase))
            return subject;
        return $"Re: {subject}";
    }

    private record EmailMessage
    {
        public string Sender { get; init; } = "";
        public string Subject { get; init; } = "";
        public string MessageId { get; init; } = "";
        public string Content { get; init; } = "";
        public IDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
    }
}
