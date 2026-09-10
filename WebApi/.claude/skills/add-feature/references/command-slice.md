# Command Slice Templates

A command use case is **one file**: `src/Web.Api/Features/{Entity}/{UseCase}.cs`, namespace `Web.Api.Features.{Entity}`. Everything below is nested inside `public static class {UseCase}`. Replace `{Entity}` (feature folder, plural, e.g. `Todos`), `{UseCase}` (e.g. `ArchiveTodo`), and the domain type names throughout.

## Full slice skeleton

```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Common.Endpoints;
using Web.Api.Common.Extensions;
using Web.Api.Common.Messaging;
using Web.Api.Database;

namespace Web.Api.Features.Todos;

public static class ArchiveTodo
{
    public sealed record Command(Guid TodoItemId) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.TodoItemId).NotEmpty();
        }
    }

    internal sealed class Handler(
        ApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        IUserContext userContext) : ICommandHandler<Command>
    {
        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            TodoItem? todoItem = await context.TodoItems
                .SingleOrDefaultAsync(
                    t => t.Id == command.TodoItemId && t.UserId == userContext.UserId,
                    cancellationToken);

            if (todoItem is null)
            {
                return Result.Failure(TodoItemErrors.NotFound(command.TodoItemId));
            }

            if (todoItem.IsArchived)
            {
                return Result.Failure(TodoItemErrors.AlreadyArchived(command.TodoItemId));
            }

            todoItem.IsArchived = true;
            todoItem.ArchivedAt = dateTimeProvider.UtcNow;

            todoItem.Raise(new TodoItemArchivedDomainEvent(todoItem.Id));

            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }

    // The endpoint is nested in this same file — see endpoint.md.
    public sealed class Endpoint : IEndpoint { /* ... */ }
}
```

## Command

Positional record for few parameters (nested):

```csharp
public sealed record Command(Guid TodoItemId) : ICommand;
```

Class with settable properties when there are many parameters (matches `CreateTodo.Command`):

```csharp
public sealed class Command : ICommand<Guid>
{
    public Guid UserId { get; set; }
    public string Description { get; set; }
    public DateTime? DueDate { get; set; }
    public List<string> Labels { get; set; } = [];
    public Priority Priority { get; set; }
}
```

- `ICommand` → handler returns `Result` (endpoint responds `204 NoContent`).
- `ICommand<TResponse>` → handler returns `Result<TResponse>` (endpoint responds `200 Ok`).
- `ICommand`/`ICommand<T>` come from `Web.Api.Common.Messaging`.

## Validator

Nested `public sealed class Validator : AbstractValidator<Command>`. Auto-registered and executed by `ValidationDecorator` before the handler runs. Because `Command` is nested, `AbstractValidator<Command>` resolves without qualification inside the slice class.

```csharp
public sealed class Validator : AbstractValidator<Command>
{
    public Validator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Priority).IsInEnum();
        RuleFor(c => c.Description).NotEmpty().MaximumLength(255);
        RuleFor(c => c.DueDate).GreaterThanOrEqualTo(DateTime.Today).When(x => x.DueDate.HasValue);
    }
}
```

## Handler

`internal sealed class Handler`, primary constructor, `ApplicationDbContext` for data access — **injected directly**, there is no `IApplicationDbContext`. Guard clauses return `Result.Failure` with feature errors; the happy path mutates, raises a domain event, saves, and returns.

```csharp
internal sealed class Handler(
    ApplicationDbContext context,
    IDateTimeProvider dateTimeProvider,
    IUserContext userContext) : ICommandHandler<Command, Guid>
{
    public async Task<Result<Guid>> Handle(Command command, CancellationToken cancellationToken)
    {
        if (userContext.UserId != command.UserId)
        {
            return Result.Failure<Guid>(UserErrors.Unauthorized());
        }

        User? user = await context.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<Guid>(UserErrors.NotFound(command.UserId));
        }

        var todoItem = new TodoItem
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Description = command.Description,
            Priority = command.Priority,
            DueDate = command.DueDate,
            Labels = command.Labels,
            IsCompleted = false,
            CreatedAt = dateTimeProvider.UtcNow
        };

        todoItem.Raise(new TodoItemCreatedDomainEvent(todoItem.Id));

        context.TodoItems.Add(todoItem);

        await context.SaveChangesAsync(cancellationToken);

        return todoItem.Id;
    }
}
```

Notes:
- `Handler` is `internal`, but the assembly scan finds it (Scrutor `AssignableTo(ICommandHandler<,>)` over `DefinedTypes`, which includes nested types).
- Ownership: either filter by `userContext.UserId` in the query (preferred) or compare explicitly and return `Result.Failure(UserErrors.Unauthorized())`. `IUserContext` is in `Web.Api.Authentication`; `UserErrors`/`User` in `Web.Api.Features.Users` (add `using Web.Api.Features.Users;` — a domain-type reference, which is allowed across features).
- `IDateTimeProvider` (from `Web.Api.Common`) for timestamps — never `DateTime.UtcNow` directly.
- If the command invalidates cached query data, inject `HybridCache` and call `cache.RemoveAsync({Feature}CacheKeys.X(...), cancellationToken)` after saving.
- For a returning command (`ICommand<Guid>`), return the value directly — `Result<T>` has an implicit conversion: `return todoItem.Id;`.

## Domain additions (if needed)

These are domain types and live in the feature folder (`Features/{Entity}/`), namespace `Web.Api.Features.{Entity}` — see the `add-entity` skill.

Error factory on the existing `{Entity}Errors` class:

```csharp
public static Error AlreadyArchived(Guid todoItemId) => Error.Problem(
    "TodoItems.AlreadyArchived",
    $"The todo item with Id = '{todoItemId}' is already archived.");
```

Error type → HTTP status (via `CustomResults.Problem`): `NotFound` → 404, `Conflict` → 409, `Problem`/`Validation` → 400, `Failure` → 500.

Domain event, one file each in `Features/{Entity}/`:

```csharp
using Web.Api.Common;

namespace Web.Api.Features.Todos;

public sealed record TodoItemArchivedDomainEvent(Guid TodoItemId) : IDomainEvent;
```

Optional event handler — a small standalone type (it isn't a use case, so it need not be nested):

```csharp
using Web.Api.Common;

namespace Web.Api.Features.Todos;

internal sealed class TodoItemArchivedDomainEventHandler : IDomainEventHandler<TodoItemArchivedDomainEvent>
{
    public Task Handle(TodoItemArchivedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        // Side effects here (notifications, projections, ...)
        return Task.CompletedTask;
    }
}
```
