using Web.Api.Common;

namespace Web.Api.Common.ValueObjects;

public static class ValueObjectsErrors
{
    // Error.Problem, not Error.Failure: a malformed value is the caller's mistake and maps to a
    // 400. Error.Failure maps to 500, which is what this class used to return.
    public static Error Empty(string valueObjectName) => Error.Problem(
        $"{valueObjectName}.Empty",
        $"{valueObjectName} cannot be empty");

    public static Error TooLong(string valueObjectName, int maxLength) => Error.Problem(
        $"{valueObjectName}.TooLong",
        $"{valueObjectName} cannot exceed {maxLength} characters");

    public static Error InvalidFormat(string valueObjectName) => Error.Problem(
        $"{valueObjectName}.InvalidFormat",
        $"{valueObjectName} has an invalid format");
}
