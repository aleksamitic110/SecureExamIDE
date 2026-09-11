namespace Web.Api.Features.Users;

// Bound from the "EmailVerification" configuration section. The defaults are the agreed ones; they
// are settings rather than constants mainly so the integration tests can switch the resend cooldown
// off instead of waiting a minute.
public sealed class EmailVerificationOptions
{
    public int CodeLifetimeMinutes { get; set; } = 15;

    // After this many wrong guesses the code stops working, even if the next guess is right. That
    // cap, not the hash, is what keeps a six-digit code from being guessed.
    public int MaxFailedAttempts { get; set; } = 5;

    // A resend inside this window is ignored and the code already sent stays valid, so the endpoint
    // cannot be used to fill someone's inbox.
    public int ResendCooldownSeconds { get; set; } = 60;
}
