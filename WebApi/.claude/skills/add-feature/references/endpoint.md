# Endpoint Templates

The endpoint is **not a separate file** — it is a nested `public sealed class Endpoint : IEndpoint` inside the slice's `public static class {UseCase}` (same file, `src/Web.Api/Features/{Entity}/{UseCase}.cs`). `IEndpoint`, `Tags`, and `.HasPermission(...)` come from `Web.Api.Common.Endpoints`; `CustomResults` from `Web.Api.Common`; the `.Match(...)` extension from `Web.Api.Common.Extensions`. Endpoints are auto-discovered by `AddEndpoints`/`MapEndpoints` (the scan reads `assembly.DefinedTypes`, which includes nested types) — no registration needed.

Because `Command`/`Query`/`Response` are siblings inside the same static class, the endpoint references them unqualified.

## Command with response body (POST → 200 + value)

```csharp
public sealed class Endpoint : IEndpoint
{
    public sealed class Request
    {
        public Guid UserId { get; set; }
        public string Description { get; set; }
        public DateTime? DueDate { get; set; }
        public List<string> Labels { get; set; } = [];
        public int Priority { get; set; }
    }

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("todos", async (
            Request request,
            ICommandHandler<Command, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new Command
            {
                UserId = request.UserId,
                Description = request.Description,
                DueDate = request.DueDate,
                Labels = request.Labels,
                Priority = (Priority)request.Priority
            };

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Todos)
        .RequireAuthorization();
    }
}
```

## Void command from route parameter (PUT/DELETE → 204)

```csharp
public sealed class Endpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("todos/{id:guid}/archive", async (
            Guid id,
            ICommandHandler<Command> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new Command(id);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .WithTags(Tags.Todos)
        .RequireAuthorization();
    }
}
```

## Query (GET → 200)

```csharp
public sealed class Endpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("todos/overdue", async (
            IQueryHandler<Query, List<Response>> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new Query();

            Result<List<Response>> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Todos)
        .RequireAuthorization();
    }
}
```

## Rules

- Routes are lowercase, plural, no leading slash: `todos`, `todos/{id:guid}`, `users/{userId:guid}/todos`. Route constraints (`:guid`) on all typed parameters.
- The nested `Request` class exists only when there's a JSON body; it maps 1:1 to the `Command` inside the lambda (enum values arrive as `int` and are cast). When the input is a single route parameter, build the `Command` directly from it — no `Request`.
- Resolve the handler interface (`ICommandHandler<...>` / `IQueryHandler<...>`, from `Web.Api.Common.Messaging`) directly as a lambda parameter — the decorated instance is injected.
- Always end with `.WithTags(Tags.{Feature})` and `.RequireAuthorization()` (or `.HasPermission(Permissions.X)` where a permission constant exists). Add the feature constant to `Tags` (in `Common/Endpoints/`) if new.
- Failures never get hand-rolled responses — `CustomResults.Problem` translates the `Error` to RFC 7807 ProblemDetails with the right status code.
- The endpoint may reference another feature's domain types, but never another feature's `Command`/`Query`/`Handler`.
