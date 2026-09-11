using Web.Api.Common;

namespace Web.Api.Features.Users;

// The code currently outstanding for an account whose address is not verified yet. There is at
// most one per user: asking for a new code overwrites it, and verifying deletes it.
public sealed class EmailVerificationCode : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    // A SHA-256 of the user id and the code, never the code itself - plain string like every other
    // piece of server-generated crypto material. Six digits are only a million possibilities, so the
    // hash does not make a leaked row unguessable; what protects a code is its short lifetime and
    // the cap on wrong attempts.
    public string CodeHash { get; set; }

    public DateTime ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }

    // When this code was issued - also what the resend cooldown is measured from.
    public DateTime CreatedAt { get; set; }
}
