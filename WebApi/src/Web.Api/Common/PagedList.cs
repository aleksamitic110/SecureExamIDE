using Microsoft.EntityFrameworkCore;

namespace Web.Api.Common;

public sealed class PagedList<T>
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public IReadOnlyList<T> Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalCount { get; init; }

    public bool HasNextPage => Page * PageSize < TotalCount;

    public bool HasPreviousPage => Page > 1;

    // The query is still an IQueryable when it gets here, so Skip and Take become part of the SQL
    // and only one page of rows is ever materialised. Passing an already-enumerated list would
    // page it in memory instead, which is the mistake this method exists to prevent.
    public static async Task<PagedList<T>> CreateAsync(
        IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);

        int totalCount = await query.CountAsync(cancellationToken);

        List<T> items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    // Paging parameters arrive from the query string, so they are normalised rather than trusted.
    // Clamping here rather than in each slice is what guarantees no caller can ask the database for
    // the whole table by sending a large enough page size.
    public static int NormalizePage(int page) => page < 1 ? 1 : page;

    public static int NormalizePageSize(int pageSize) => pageSize switch
    {
        < 1 => DefaultPageSize,
        > MaxPageSize => MaxPageSize,
        _ => pageSize
    };
}
