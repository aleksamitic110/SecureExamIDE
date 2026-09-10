# Test Templates

Stack: xUnit + Shouldly + NSubstitute (unit), FluentValidation.TestHelper (validators), WebApplicationFactory + Testcontainers (integration). Every new use case gets all three. Because the slice is one static class, its members are referenced qualified: `CreateTodo.Command`, `new CreateTodo.Handler(...)`, `new CreateTodo.Validator()`.

`GlobalUsings.cs` already imports `Xunit`, `NSubstitute`, and `Shouldly` — don't re-add those. `Result`/`Error` come from `Web.Api.Common`; the slice, entity, errors, and domain events from `Web.Api.Features.{Entity}`.

## Handler unit tests

`tests/Web.Api.UnitTests/{Feature}/{UseCase}HandlerTests.cs`. Inherit `BaseHandlerTest` — it provides `CreateDbContext()` (a fresh in-memory `ApplicationDbContext`) and `CreateCache()` (a real `HybridCache`). There is no `IApplicationDbContext`/`TestDbContext` in this template: handlers take the concrete `ApplicationDbContext`, so tests use it directly. Mock only interfaces (`IUserContext`, `IDateTimeProvider`) with NSubstitute.

Cover: each failure path (one test per guard clause) and the happy path including persisted state and raised domain events.

```csharp
using Microsoft.EntityFrameworkCore;
using Web.Api.Authentication;
using Web.Api.Common;
using Web.Api.Database;
using Web.Api.Features.Todos;

namespace Web.Api.UnitTests.Todos;

public sealed class ArchiveTodoHandlerTests : BaseHandlerTest
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTodoDoesNotExist()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(UserId);
        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();

        var command = new ArchiveTodo.Command(Guid.NewGuid());
        var handler = new ArchiveTodo.Handler(context, dateTimeProvider, userContext);

        // Act
        Result result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(TodoItemErrors.NotFound(command.TodoItemId));
    }

    [Fact]
    public async Task Handle_Should_ArchiveTodoAndRaiseDomainEvent_WhenValid()
    {
        // Arrange
        await using ApplicationDbContext context = CreateDbContext();
        var todoItem = new TodoItem
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Description = "Archive me",
            CreatedAt = DateTime.UtcNow
        };
        context.TodoItems.Add(todoItem);
        await context.SaveChangesAsync();

        IUserContext userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(UserId);
        IDateTimeProvider dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(DateTime.UtcNow);

        var command = new ArchiveTodo.Command(todoItem.Id);
        var handler = new ArchiveTodo.Handler(context, dateTimeProvider, userContext);

        // Act
        Result result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        TodoItem archived = await context.TodoItems.SingleAsync(t => t.Id == todoItem.Id);
        archived.IsArchived.ShouldBeTrue();
        archived.DomainEvents.ShouldContain(e => e is TodoItemArchivedDomainEvent);
    }
}
```

Conventions:
- Test names: `Handle_Should_{Outcome}_When{Condition}`.
- `// Arrange` / `// Act` / `// Assert` comments in every test.
- Assert failures by comparing the exact error: `result.Error.ShouldBe(TodoItemErrors.NotFound(id))`.
- New entity properties flow through automatically. Adding a whole new `DbSet` is an `add-entity` step (it lands on `ApplicationDbContext`, which the in-memory test context uses as-is).

## Validator tests

`tests/Web.Api.UnitTests/{Feature}/{Feature}ValidatorsTests.cs` (extend the existing file if present). One class covers all validators of a feature; each validator is the slice's nested `Validator`.

```csharp
using FluentValidation.TestHelper;
using Web.Api.Features.Todos;

// ...
private readonly CreateTodo.Validator _createValidator = new();

[Fact]
public void CreateValidator_Should_HaveError_WhenDescriptionIsEmpty()
{
    var command = new CreateTodo.Command
    {
        UserId = Guid.NewGuid(),
        Description = string.Empty,
        Priority = Priority.Low
    };

    TestValidationResult<CreateTodo.Command> result = _createValidator.TestValidate(command);

    result.ShouldHaveValidationErrorFor(c => c.Description);
}
```

Cover each rule's failure plus one fully-valid command (`ShouldNotHaveAnyValidationErrors`).

## Integration tests

`tests/IntegrationTests/{Feature}/{Feature}Tests.cs` (extend the existing file if present). Inherit `BaseIntegrationTest(factory)` — it runs the real API against a Testcontainers Postgres and provides `HttpClient`, `RegisterAndLoginAsync()`, and `Authenticate(token)`. Tests go through real HTTP, never call handlers directly, so they don't reference the slice types — define private response DTO records inside the test class (see `TodosTests.TodoDto`).

```csharp
[Fact]
public async Task ArchiveTodo_Should_MarkTodoAsArchived()
{
    // Arrange
    (Guid userId, AccessTokens tokens) = await RegisterAndLoginAsync();
    Authenticate(tokens.AccessToken);

    var createRequest = new
    {
        userId,
        description = "Todo to archive",
        labels = Array.Empty<string>(),
        priority = 1
    };
    HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("todos", createRequest);
    createResponse.EnsureSuccessStatusCode();
    Guid todoId = await createResponse.Content.ReadFromJsonAsync<Guid>();

    // Act
    HttpResponseMessage response = await HttpClient.PutAsync($"todos/{todoId}/archive", null);

    // Assert
    response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
}
```

Minimum coverage per endpoint: one unauthorized test (no token → 401) if it's a new route family, one happy-path test asserting observable state via a follow-up GET, and one failure translation test (e.g. unknown id → 404) when the handler has failure paths.

## Run

```
dotnet test
```

Integration tests need Docker running (Testcontainers).
