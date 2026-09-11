using Web.Api.Common;
using Web.Api.Common.ValueObjects;

namespace Web.Api.Features.Users;

public sealed class User : Entity
{
    public Guid Id { get; set; }
    public Email Email { get; set; }
    public PersonName FirstName { get; set; }
    public PersonName LastName { get; set; }

    // Left as a plain string on purpose: it is opaque server-generated crypto material with no
    // domain rule a value object could enforce, so wrapping it would add noise and no safety.
    public string PasswordHash { get; set; }

    public Role Role { get; set; }

    // The student's faculty index number. Professors do not have one, so this is null for them.
    public IndexNumber? IndexNumber { get; set; }

    // Cleared when an account is revoked. An inactive user keeps every credential it was ever
    // issued but is granted no permissions, so all protected endpoints reject it.
    public bool IsActive { get; set; }

    // When the address was confirmed with the code sent to it, and null until then. An unverified
    // account can neither log in nor exchange its device credential for a token, so the only thing
    // it can do is verify.
    public DateTime? EmailVerifiedAt { get; set; }
}
