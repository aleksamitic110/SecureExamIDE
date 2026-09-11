namespace Web.Api.Notifications;

// Sending mail is best effort and never throws. Everything this API mails - a verification code -
// is something the user can simply ask for again, so a mail server outage must not turn an action
// that has already been committed, such as a registration, into an error. Failures are logged.
public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default);
}
