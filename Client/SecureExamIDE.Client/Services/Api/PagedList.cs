namespace SecureExamIDE.Client.Services.Api;

// One page of a list endpoint, in the shape the API's PagedList<T> is written.
public sealed record PagedList<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasNextPage,
    bool HasPreviousPage);
