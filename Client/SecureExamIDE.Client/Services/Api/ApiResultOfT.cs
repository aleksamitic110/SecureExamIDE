namespace SecureExamIDE.Client.Services.Api;

// The outcome of an API call that returns a body. Value may only be read after IsSuccess.
public sealed class ApiResult<T> : ApiResult
{
    private readonly T? _value;

    internal ApiResult(T? value, ApiError? error)
        : base(error)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed result cannot be read.");
}
