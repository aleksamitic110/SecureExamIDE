using System.Diagnostics.CodeAnalysis;

namespace SecureExamIDE.Client.Services.Api;

// The outcome of an API call that has no body to return. Expected failures - a wrong code, a
// taken e-mail, an unreachable server - come back as values, never as exceptions, the same way
// the server's handlers return Result.
public class ApiResult
{
    protected ApiResult(ApiError? error)
    {
        Error = error;
    }

    public ApiError? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static ApiResult Success() => new(null);

    public static ApiResult Failure(ApiError error) => new(error);

    public static ApiResult<T> Success<T>(T value) => new(value, null);

    public static ApiResult<T> Failure<T>(ApiError error) => new(default, error);
}
