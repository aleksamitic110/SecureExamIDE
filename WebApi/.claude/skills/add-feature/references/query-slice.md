# Query Slice Templates

A query use case is **one file**: `src/Web.Api/Features/{Entity}/{UseCase}.cs`, namespace `Web.Api.Features.{Entity}`. Everything below is nested inside `public static class {UseCase}`. Queries are reads: no validator, no domain events, no `SaveChangesAsync`.

## Full slice skeleton

```csharp
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Todos;

public static class GetOverdueTodos
{
    public sealed record Query : IQuery<List<Response>>;

    public sealed class Response
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Description { get; set; }
        public DateTime? DueDate { get; set; }
        public List<string> Labels { get; set; } = [];
        public bool IsCompleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IUserContext userContext,
        IDateTimeProvider dateTimeProvider) : IQueryHandler<Query, List<Response>>
    {
        public async Task<Result<List<Response>>> Handle(
            Query query,
            CancellationToken cancellationToken)
        {
            List<Response> todos = await context.TodoItems
                .Where(todoItem => todoItem.UserId == userContext.UserId &&
                                   !todoItem.IsCompleted &&
                                   todoItem.DueDate < dateTimeProvider.UtcNow)
                .Select(todoItem => new Response
                {
                    Id = todoItem.Id,
                    UserId = todoItem.UserId,
                    Description = todoItem.Description,
                    DueDate = todoItem.DueDate,
                    Labels = todoItem.Labels,
                    IsCompleted = todoItem.IsCompleted,
                    CreatedAt = todoItem.CreatedAt,
                    CompletedAt = todoItem.CompletedAt
                })
                .ToListAsync(cancellationToken);

            return todos;
        }
    }

    // The endpoint is nested in this same file — see endpoint.md.
    public sealed class Endpoint : IEndpoint { /* ... */ }
}
```

## Query

Parameterless (nested):

```csharp
public sealed record Query : IQuery<List<Response>>;
```

With parameters: `public sealed record Query(Guid TodoItemId) : IQuery<Response>;`

`IQuery<T>` / `IQueryHandler<Q, T>` come from `Web.Api.Common.Messaging`.

## Response DTO

A nested `public sealed class Response` — flat, serialization-friendly, never a domain entity. Each slice owns its own `Response`; do not share DTOs across slices even if they look identical today. Because it is nested, the handler and endpoint reference it unqualified as `Response`.

## Handler

`internal sealed class Handler`, `ApplicationDbContext` injected directly (no `IApplicationDbContext`). Scope to the current user and project with `.Select` straight into the nested `Response`. For single-item queries, return `Result.Failure<Response>(TodoItemErrors.NotFound(id))` when nothing matches.

`IUserContext` is in `Web.Api.Authentication`; `IDateTimeProvider`, `Result` in `Web.Api.Common`; `TodoItemErrors` in the feature namespace.

## Caching (optional, hot reads only)

The cache-keys class is a feature-level helper (not a use case), so it is its own small file in the feature folder:

```csharp
namespace Web.Api.Features.Todos;

internal static class TodoCacheKeys
{
    internal static string ById(Guid userId, Guid todoItemId) => $"todos-{userId}-{todoItemId}";
}
```

Inject `HybridCache` into the handler and wrap the database query:

```csharp
Response? todo = await cache.GetOrCreateAsync(
    TodoCacheKeys.ById(userId, query.TodoItemId),
    async cancellation => await context.TodoItems
        .Where(...)
        .Select(...)
        .SingleOrDefaultAsync(cancellation),
    cancellationToken: cancellationToken);
```

Every command that mutates the cached data must invalidate the same key with `cache.RemoveAsync(...)`. If you can't enumerate the affected keys, don't cache.
