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

    public static readonly Error InvalidRefreshToken = Error.Problem(
        "Users.InvalidRefreshToken",
        "The provided refresh token is invalid or has expired");
}
