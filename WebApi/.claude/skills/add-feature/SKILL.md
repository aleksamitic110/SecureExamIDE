---
name: add-feature
description: Scaffold a complete Vertical Slice Architecture feature — one file with a nested command or query, custom handler, FluentValidation validator, minimal API endpoint, and tests (unit, validator, integration). Use when the user asks to add a feature, use case, command, query, or endpoint to this Vertical Slice Architecture template.
argument-hint: <feature description, e.g. "archive a todo item" or "get todos due this week">
---

# Add a Feature (Vertical Slice)

Scaffold a full use case following this template's conventions: a single self-contained file under `Features/{Entity}/` that holds the command/query, its custom handler, its validator, and its minimal-API endpoint. No MediatR — this codebase uses its own `ICommand`/`IQuery` abstractions (`Web.Api.Common.Messaging`) with Scrutor-registered handlers and decorators.

## The one-file rule

A use case is **one file**: `src/Web.Api/Features/{Entity}/{UseCase}.cs`, namespace `Web.Api.Features.{Entity}`. That file is a `public static class {UseCase}` whose nested members are the whole slice:

```csharp
public static class ArchiveTodo
{
    public sealed record Command(...) : ICommand;              // or Query : IQuery<T>
    public sealed class Validator : AbstractValidator<Command> { ... }  // commands only
    internal sealed class Handler(...) : ICommandHandler<Command> { ... }
    public sealed class Endpoint : IEndpoint { ... }
}
```

Everything for the slice lives here — there is no separate Application/Domain/Infrastructure project and no separate `Endpoints/` folder. Cross-cutting code lives in `Common/`, `Database/`, `Authentication/`, `Authorization/`; each feature lives in `Features/{Entity}/`.

## Workflow

1. **Classify the use case.** A state change is a **command**; a read is a **query**. Name the slice with the use-case verb + entity, e.g. `ArchiveTodo`, `GetOverdueTodos` — that name is the file, the folder-mate, and the static class.
2. **Check the feature's domain types.** If the entity, its `{Entity}Errors` class, or a needed domain event doesn't exist in `Features/{Entity}/`, add it first (see the `add-entity` skill). Commands that change state should raise a domain event via `entity.Raise(...)`.
3. **Create the slice file** at `src/Web.Api/Features/{Entity}/{UseCase}.cs` with the nested `Command`/`Query`, `Validator` (commands only), `Handler`, and `Endpoint`. Templates: [references/command-slice.md](references/command-slice.md), [references/query-slice.md](references/query-slice.md), and [references/endpoint.md](references/endpoint.md).
4. **Write tests** — handler unit tests, validator tests, and an integration test. Templates: [references/tests.md](references/tests.md).
5. **Verify:** `dotnet build` then `dotnet test`. Everything must pass.

## Non-negotiable conventions

- **File = use case.** One file per use case under `src/Web.Api/Features/{Entity}/` (e.g. `Features/Todos/ArchiveTodo.cs`), containing the whole slice as nested types of `public static class {UseCase}`.
- **Handlers are `internal sealed`** with primary constructors, nested as `Handler`, implementing `ICommandHandler<Command>`, `ICommandHandler<Command, TResponse>`, or `IQueryHandler<Query, TResponse>` (from `Web.Api.Common.Messaging`).
- **No manual DI registration.** Handlers, validators, and endpoints are discovered by assembly scanning — Scrutor (`AssignableTo(ICommandHandler<,>)`), `AddValidatorsFromAssembly(includeInternalTypes: true)`, and `AddEndpoints`. Nested types are in `assembly.DefinedTypes`, so they are found automatically. Never wire up a new slice by hand.
- **Return `Result` / `Result<T>`, never throw** for expected failures. Errors come from static factory methods on `{Entity}Errors` (in the feature folder) with codes like `"Todos.NotFound"`. `Result`, `Error`, `ErrorType` live in `Web.Api.Common`.
- **Validation lives in the nested `Validator`** (FluentValidation). It runs automatically via `ValidationDecorator` — the handler never validates input shape itself. Queries have no validators (the decorator only wraps commands).
- **Data access via `ApplicationDbContext` directly** (from `Web.Api.Database`). There is **no `IApplicationDbContext`** — inject the concrete `ApplicationDbContext` into the handler.
- **Authorization check** in handlers that act on user-owned data: compare `IUserContext.UserId` (from `Web.Api.Authentication`) and return `UserErrors.Unauthorized()` on mismatch, or filter queries by `userContext.UserId`.
- **Queries project directly to a nested `Response` DTO** with `.Select(...)` — never return domain entities. Cache hot reads with `HybridCache` using a `{Feature}CacheKeys` static class; invalidate in the commands that mutate the cached data.
- **The nested `Endpoint`** implements `IEndpoint`, resolves the handler interface directly from DI, uses `result.Match(Results.Ok, CustomResults.Problem)` (or `Results.NoContent` for void commands), tags with the `Tags` class, and calls `.RequireAuthorization()`.
- **No cross-feature coupling.** A slice may reference another feature's **domain types** (e.g. `Todos` uses `User`/`UserErrors` via `using Web.Api.Features.Users;`) and the shared `Common`/`Database`/`Authentication`/`Authorization` code. It must **never** reference another feature's `Command`/`Query`/`Handler`/`Validator`/`Endpoint`.

## Naming reference

| Artifact | Pattern | Example |
|---|---|---|
| Slice file & class | `{Verb}{Entity}` / `Get{X}` | `Features/Todos/ArchiveTodo.cs` → `public static class ArchiveTodo` |
| Command | nested `Command` | `ArchiveTodo.Command` |
| Query | nested `Query` | `GetOverdueTodos.Query` |
| Handler | nested `internal sealed Handler` | `ArchiveTodo.Handler` |
| Validator | nested `Validator` | `ArchiveTodo.Validator` |
| Response | nested `Response` | `GetOverdueTodos.Response` |
| Endpoint | nested `Endpoint : IEndpoint` | `ArchiveTodo.Endpoint` |
| Unit test | `{UseCase}HandlerTests` | `ArchiveTodoHandlerTests` |
| Test method | `Handle_Should_{Outcome}_When{Condition}` | `Handle_Should_ReturnNotFound_WhenTodoDoesNotExist` |
