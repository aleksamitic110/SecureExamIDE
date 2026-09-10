---
name: add-entity
description: Add a new domain entity to the Vertical Slice Architecture template — entity class, error catalog, domain events, EF Core configuration, DbContext wiring, and migration. Use when the user asks to add an entity, aggregate, domain model, or table.
argument-hint: <entity description, e.g. "Project with a name, owner, and list of todos">
---

# Add a Domain Entity

Create a new entity and wire it through the single `Web.Api` project, following the `TodoItem` pattern. The entity and its domain types live with their feature; only the EF configuration and the `DbSet` live in `Database/`.

## Files to create/modify

1. **Entity** — `src/Web.Api/Features/{Entity}/{Entity}.cs`

```csharp
using Web.Api.Common;

namespace Web.Api.Features.Projects;

public sealed class Project : Entity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

`sealed class`, inherits `Entity` (from `Web.Api.Common` — gives it `DomainEvents` + `Raise(...)`), `Guid Id`, plain settable properties, collections initialized with `= [];`.

2. **Error catalog** — `src/Web.Api/Features/{Entity}/{Entity}Errors.cs`

```csharp
using Web.Api.Common;

namespace Web.Api.Features.Projects;

public static class ProjectErrors
{
    public static Error NotFound(Guid projectId) => Error.NotFound(
        "Projects.NotFound",
        $"The project with the Id = '{projectId}' was not found");
}
```

Codes are `"{FeaturePlural}.{Reason}"`. Pick the factory by semantics: `Error.NotFound` (404), `Error.Conflict` (409), `Error.Problem` (400), `Error.Failure` (500). `Error`/`ErrorType` are in `Web.Api.Common`.

3. **Domain events** — one record per file, `src/Web.Api/Features/{Entity}/{Entity}{PastTenseVerb}DomainEvent.cs`

```csharp
using Web.Api.Common;

namespace Web.Api.Features.Projects;

public sealed record ProjectCreatedDomainEvent(Guid ProjectId) : IDomainEvent;
```

Create at minimum the `Created` event; add others as commands need them. Events carry ids, not entities. `IDomainEvent` is in `Web.Api.Common`.

4. **EF configuration** — `src/Web.Api/Database/Configurations/{Entity}Configuration.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Web.Api.Features.Projects;
using Web.Api.Features.Users;

namespace Web.Api.Database.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.OwnerId);
    }
}
```

Relationships are configured shadow-style (`HasOne<User>().WithMany()`) — entities hold foreign-key ids, not navigation properties. Configurations are picked up automatically by `ApplyConfigurationsFromAssembly` in `ApplicationDbContext`. Referencing another feature's entity here (e.g. `User`) is fine — it's a domain-type reference, not a cross-slice handler dependency.

5. **DbContext wiring** — add `DbSet<{Entity}> {Plural}` to `src/Web.Api/Database/ApplicationDbContext.cs`. This is the **only** place a `DbSet` is declared — there is no `IApplicationDbContext` in this template. Handlers and unit tests use the concrete `ApplicationDbContext`, so the new set is available everywhere automatically.

6. **Migration** — from the repo root (single project, so it's both `--project` and `--startup-project`):

```
dotnet ef migrations add Add_{Plural} --project src/Web.Api --startup-project src/Web.Api
```

Migration names are `PascalCase_With_Underscores` (see `Add_RefreshTokens`).

## Rules

- The entity is a plain domain type in its feature folder — no EF attributes, no persistence concerns on it. Keys, conversions, and relationships live exclusively in the `Database/Configurations/` configuration.
- Entity, errors, and domain events go in `Features/{Entity}/` (namespace `Web.Api.Features.{Entity}`); the EF configuration goes in `Database/Configurations/`; the `DbSet` on `ApplicationDbContext`.
- Domain types are shareable across features (a slice in another feature may reference this entity), but never reference another feature's `Command`/`Handler`.
- Run `dotnet build` and `dotnet test` when done.
- If the user also wants use cases for the entity, continue with the `add-feature` skill.
