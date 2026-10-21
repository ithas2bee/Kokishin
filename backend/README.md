# Backend

The backend is a .NET solution containing an ASP.NET Core API and three class
libraries. Build it from this directory with:

```sh
dotnet build Kokishin.Backend.sln
```

## Projects and responsibilities

- **Kokishin.Api** (`src/Kokishin.Api`): HTTP delivery and the composition root.
  It hosts the application, handles HTTP concerns, and composes application and
  infrastructure services. It contains no product endpoints yet; keep business
  rules and use-case decisions out of this project.
- **Kokishin.Application** (`src/Kokishin.Application`): application use cases
  and the abstractions those use cases need from external systems. Keep
  infrastructure implementations and HTTP concerns out of this project.
- **Kokishin.Domain** (`src/Kokishin.Domain`): business concepts and rules,
  independent of delivery, persistence, frameworks, and other application
  projects.
- **Kokishin.Infrastructure** (`src/Kokishin.Infrastructure`): implementations
  of application/domain abstractions and external technical concerns. Add
  persistence or other integrations only when required by an assigned feature.

## Project references

The permitted direct project references are:

| Project | May reference |
| --- | --- |
| `Kokishin.Domain` | None |
| `Kokishin.Application` | `Kokishin.Domain` |
| `Kokishin.Infrastructure` | `Kokishin.Application`, `Kokishin.Domain` |
| `Kokishin.Api` | `Kokishin.Application`, `Kokishin.Infrastructure` |

These rules were checked against the `ProjectReference` entries in the four
project files. Do not add references outside this table: in particular, Domain
must not depend on another project, Application must not depend on API or
Infrastructure, and API must not take a direct dependency on Domain.

## Coding standards

- Follow conventions in nearby code first. When there is no established
  convention, use standard .NET naming: PascalCase for types and public members,
  `I`-prefixed interfaces, camelCase for parameters and locals, and
  `_camelCase` for private fields.
- Give each class one clear responsibility. Keep types small and cohesive; avoid
  classes that mix HTTP handling, use-case orchestration, business rules, and
  external-system access.
- Put business invariants and domain behavior in Domain. Put application
  workflows and ports/abstractions in Application. Keep HTTP request/response
  handling in API and external-system implementations in Infrastructure.
- Keep dependencies pointing inward through the permitted project references.
  Application code should depend on abstractions it owns, not implementation
  details. Do not introduce framework or provider dependencies into Domain.
- Validate inputs at the boundary where they enter a layer and preserve domain
  invariants in Domain. Do not silently swallow errors or expose internal
  exception details to API clients; translate failures to an appropriate
  boundary response using established project patterns.
- Prefer focused changes and reuse existing patterns. Add abstractions only
  where they clarify a real boundary or responsibility.

This foundation does not yet include product endpoints, feature use cases,
database providers, an ORM, or test projects. Do not describe or implement
planned functionality as if it already exists.

## Build and test

From this directory, build the solution:

```sh
dotnet build Kokishin.Backend.sln
```

Run the solution's tests when test projects are present:

```sh
dotnet test Kokishin.Backend.sln
```

There are currently no test projects or configured backend lint/format
commands. For a backend change, build the solution, run relevant existing tests
(or report that none exist), and review the project references and changed
behavior before submitting.
