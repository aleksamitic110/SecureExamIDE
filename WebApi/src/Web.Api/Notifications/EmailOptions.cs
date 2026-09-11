namespace Web.Api.Notifications;

// Bound from the "Email" configuration section. The host, and any credentials, come from .env or
// user settings, never from a committed file; with no host configured the API falls back to
// LoggingEmailSender.
public sealed class EmailOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 25;

    // Off for Mailpit, which speaks plain SMTP; on for a real provider, where MailKit negotiates
    // TLS itself.
    public bool UseSsl { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "SecureExamIDE";
}
