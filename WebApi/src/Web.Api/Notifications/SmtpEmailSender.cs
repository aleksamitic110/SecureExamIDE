using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Web.Api.Notifications;

// Sends mail through any SMTP server - Mailpit in development, a real provider in production. A new
// connection per message is plenty for what this API sends: one code per registration or resend.
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
    {
        EmailOptions settings = options.Value;

        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();

        // Best effort, as IEmailSender promises: whatever goes wrong talking to the mail server is
        // logged, and the caller carries on - the user can ask for the mail again. Only a
        // cancellation is allowed through, because that is the caller's own decision.
#pragma warning disable CA1031
        try
        {
            await client.ConnectAsync(
                settings.Host,
                settings.Port,
                settings.UseSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None,
                cancellationToken);

            if (!string.IsNullOrEmpty(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Sending the e-mail \"{Subject}\" failed", subject);
        }
#pragma warning restore CA1031
    }
}
