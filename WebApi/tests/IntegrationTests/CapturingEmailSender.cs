using System.Collections.Concurrent;
using Web.Api.Notifications;

namespace IntegrationTests;

// Stands in for the SMTP sender during the test run and keeps every message, so a test can read
// the verification code the API mailed exactly as a student would read it from their inbox.
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(new SentEmail(recipient, subject, body));

        return Task.CompletedTask;
    }

    public IReadOnlyList<SentEmail> SentTo(string recipient) =>
        [.. _sent.Where(e => string.Equals(e.Recipient, recipient, StringComparison.OrdinalIgnoreCase))];

    public sealed record SentEmail(string Recipient, string Subject, string Body);
}
