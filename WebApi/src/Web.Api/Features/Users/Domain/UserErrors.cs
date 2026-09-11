using Web.Api.Common;

namespace Web.Api.Features.Users;

public static class UserErrors
{
    public static Error NotFound(Guid userId) => Error.NotFound(
        "Users.NotFound",
        $"The user with the Id = '{userId}' was not found");

    public static Error Forbidden() => Error.Forbidden(
        "Users.Forbidden",
        "You are not allowed to perform this action.");

    public static readonly Error NotFoundByEmail = Error.NotFound(
        "Users.NotFoundByEmail",
        "The user with the specified email was not found");

    public static readonly Error EmailNotUnique = Error.Conflict(
        "Users.EmailNotUnique",
        "The provided email is not unique");

    public static readonly Error IndexNumberNotUnique = Error.Conflict(
        "Users.IndexNumberNotUnique",
        "The provided index number is already registered to another student");

    public static readonly Error InvalidProfessorRegistrationCode = Error.Problem(
        "Users.InvalidProfessorRegistrationCode",
        "The provided professor registration code is invalid");

    // Only ever returned to a caller who has already proved they own the account - a correct
    // password, or a valid device credential - so it tells a stranger nothing.
    public static readonly Error EmailNotVerified = Error.Forbidden(
        "Users.EmailNotVerified",
        "The e-mail address has not been verified yet. Enter the code that was sent to it.");

    // One answer for every way verification can fail - unknown address, already verified, wrong,
    // expired or exhausted code - so the endpoint cannot be used to learn which addresses exist.
    public static readonly Error InvalidVerificationCode = Error.Problem(
        "Users.InvalidVerificationCode",
        "The verification code is invalid or has expired. Request a new one if needed.");

    public static readonly Error InvalidRefreshToken = Error.Problem(
        "Users.InvalidRefreshToken",
        "The provided refresh token is invalid or has expired");
}
