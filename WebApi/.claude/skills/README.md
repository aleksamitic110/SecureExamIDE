# Vertical Slice Architecture Agent Skills for Claude Code

A skill pack that teaches Claude Code the conventions of the Vertical Slice Architecture template — so every feature it builds looks like you wrote it: one file per use case, custom command/query handlers (no MediatR), Result-based error handling, direct `ApplicationDbContext` access, minimal API endpoints, and full test coverage.

## What's inside

| Skill | Invoke with | What it does |
|---|---|---|
| **add-feature** | `/add-feature archive a todo item` | Scaffolds a complete vertical slice in one file: nested command/query, handler, validator, endpoint, plus unit + validator + integration tests. |
| **add-entity** | `/add-entity Project with a name and owner` | Adds a domain entity end to end: entity, error catalog, domain events, EF configuration, DbContext wiring, migration. |
| **add-tests** | `/add-tests CreateTodo` | Backfills handler, validator, and integration tests for existing use cases. |
| **vsa-review** | `/vsa-review` | Reviews pending changes against the template's conventions: one-file slices, slice isolation, error handling, security, caching, and test coverage. |

You don't have to invoke them explicitly — once installed, Claude Code picks the right skill automatically when you say things like "add an endpoint to snooze a todo."

## Installation

The skills live in `.claude/skills/`. If you cloned the template, they're already active — just open the repo in Claude Code.

To use them in another project based on this template, copy the folder:

```
your-project/
└── .claude/
    └── skills/
        ├── add-feature/
        ├── add-entity/
        ├── add-tests/
        └── vsa-review/
```

Works with both the standard and the Aspire variants of the template.

## Try it

```
/add-feature snooze a todo until a given date
```

Claude will create the one-file slice — the command, validator, handler (with ownership check, domain event, and cache invalidation), and the endpoint — plus the three test types, then build and run the tests.

## Customizing

Each skill is a plain Markdown file (`SKILL.md`, plus templates under `references/`). Prefer a different folder layout, records everywhere, a different test stack? Edit the templates once and every future feature follows suit. The skills are the executable version of your team's conventions doc.

---

Built for the [Vertical Slice Architecture template](https://www.milanjovanovic.tech) by Milan Jovanović.
