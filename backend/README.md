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

This foundation does not yet include product endpoints, feature use cases, or
test projects. Do not describe or implement planned functionality as if it
already exists.

## Persistence

Persistence uses **Entity Framework Core 10 with SQLite**. SQLite keeps local
development self-contained and stores data in a file, avoiding a separately
managed database service. Its single-writer concurrency and local-file
deployment model are not a fit for every production workload; deployments
requiring a server database or higher concurrency should change the provider
registration in Infrastructure. Provider configuration and `KokishinDbContext`
live in `Kokishin.Infrastructure`; neither Application nor Domain references EF
Core or a database provider.

## Configuration

The API uses the standard ASP.NET Core configuration system. Shared, non-secret
settings belong in `src/Kokishin.Api/appsettings.json`; environment-specific
settings are loaded from `appsettings.{Environment}.json`. The checked-in
`appsettings.Development.json` supplies `Data Source=kokishin.db`, a local
SQLite file that requires no credentials. The `http` and `https` launch profiles
select the Development environment.

Run locally from `backend/` with:

```sh
dotnet run --project src/Kokishin.Api
```

The Infrastructure registration requires a non-empty SQLite connection string
named `Kokishin` and validates its format and data source during startup. For
Production, set `ASPNETCORE_ENVIRONMENT=Production` and provide the connection
string through the environment or a secret-management provider; do not commit
production values to `appsettings*.json`. ASP.NET Core maps double underscores
in environment variable names to configuration colons:

```sh
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__Kokishin='Data Source=<deployment-managed-path>'
dotnet run --no-launch-profile --project src/Kokishin.Api
```

Replace `<deployment-managed-path>` with the SQLite file location managed by
the deployment. Keep credentials and other secrets in the deployment's secret
store and inject them at runtime. A missing, empty, malformed, or source-less
connection string stops startup with an error identifying the required setting.

The initial migration is an empty baseline: it creates only EF Core's migration
metadata tables and no product tables. From `backend/`, install the EF
command-line tool once and initialize the local database:

```sh
dotnet tool install --global dotnet-ef --version 10.0.13
dotnet ef database update \
  --project src/Kokishin.Infrastructure \
  --startup-project src/Kokishin.Api
```

Then check the configured database connection while the API is running:

```sh
dotnet run --project src/Kokishin.Api
curl --fail http://localhost:5108/health/database
```

The health endpoint returns success only when EF Core can connect to the
configured database. The API does not create or migrate schemas on startup.
Create and apply each later schema change explicitly from `backend/`:

```sh
dotnet ef migrations add AddPersistenceModel \
  --project src/Kokishin.Infrastructure \
  --startup-project src/Kokishin.Api \
  --output-dir Persistence/Migrations
dotnet ef database update \
  --project src/Kokishin.Infrastructure \
  --startup-project src/Kokishin.Api
```

Commit generated migrations as schema changes. The SQLite database and its
journal files are local development data and are ignored by Git.

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
