namespace SecureExamIDE.Client.Services.Api;

// A failed call, read from the problem details the API returns. Code is the server's error code
// ("Users.EmailNotVerified"), which is what the client branches on; Detail is the English message
// written for a person. StatusCode 0 means no response arrived at all.
public sealed record ApiError(int StatusCode, string Code, string Detail, IReadOnlyList<string> ValidationMessages)
{
    public bool IsUnreachable => StatusCode == 0;

    // What to show on screen: the individual validation messages when the server sent any,
    // otherwise its detail.
    public string Message => ValidationMessages.Count > 0
        ? string.Join(Environment.NewLine, ValidationMessages)
        : Detail;

    public static ApiError Unreachable(string detail) => new(0, UnreachableCode, detail, []);

    public const string UnreachableCode = "Client.ServerUnreachable";
}
