namespace NanoBot.Core.Configuration;

public class EmailConfig
{
    public bool Enabled { get; set; }
    public bool ConsentGranted { get; set; }

    public string ImapHost { get; set; } = string.Empty;
    public int ImapPort { get; set; } = 993;
    public string ImapUsername { get; set; } = string.Empty;
    public string ImapPassword { get; set; } = string.Empty;
    public string ImapMailbox { get; set; } = "INBOX";
    public bool ImapUseSsl { get; set; } = true;

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public bool SmtpUseTls { get; set; } = true;
    public string FromAddress { get; set; } = string.Empty;

    public bool AutoReplyEnabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 30;
    public bool MarkSeen { get; set; } = true;
    public int MaxBodyChars { get; set; } = 12000;
    public IReadOnlyList<string> AllowFrom { get; set; } = Array.Empty<string>();
}
