# Kokishin

Kokishin is a learning management system and teaching portfolio project. It is
being developed to support course creation, teaching activities, and the
presentation of teaching practice.

## Solution structure

The .NET solution and projects are located at the repository root:

```text
Kokishin.sln
Kokishin.Api/
Kokishin.Application/
Kokishin.Domain/
Kokishin.Infrastructure/
Kokishin.Tests/
docs/
```

- **Kokishin.Api** is the ASP.NET Core HTTP host and composition root. It
  configures the Application and Infrastructure services.
- **Kokishin.Application** contains application use cases and the abstractions
  they require.
- **Kokishin.Domain** contains business concepts and rules, independent of the
  other application projects and infrastructure frameworks.
- **Kokishin.Infrastructure** implements persistence and other external
  technical concerns. It contains the EF Core SQLite configuration, database
  context, migrations, and database health check.
- **Kokishin.Tests** contains automated tests for the application.

The allowed project references preserve the inward dependency direction:

| Project | Allowed project references |
| --- | --- |
| `Kokishin.Domain` | None |
| `Kokishin.Application` | `Kokishin.Domain` |
| `Kokishin.Infrastructure` | `Kokishin.Application`, `Kokishin.Domain` |
| `Kokishin.Api` | `Kokishin.Application`, `Kokishin.Infrastructure` |
| `Kokishin.Tests` | Projects required by its tests |

Open [`Kokishin.sln`](Kokishin.sln) from the repository root in Visual Studio.

## Build and run

Run these commands from the repository root:

```sh
dotnet build Kokishin.sln
dotnet test Kokishin.sln
dotnet run --project Kokishin.Api
```

The API uses the standard ASP.NET Core configuration system. Shared,
non-secret settings belong in `Kokishin.Api/appsettings.json`. Environment
settings belong in `Kokishin.Api/appsettings.{Environment}.json`; the checked-in
`appsettings.Development.json` configures the local SQLite database at
`Data Source=kokishin.db`. The `http` and `https` launch profiles select the
Development environment.

Infrastructure requires a non-empty SQLite connection string named `Kokishin`
and validates its format and data source during startup. For production, provide
the connection string through environment variables or a secret-management
provider; do not commit production credentials or environment-specific secrets:

```sh
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__Kokishin='Data Source=<deployment-managed-path>'
dotnet run --no-launch-profile --project Kokishin.Api
```

ASP.NET Core maps double underscores in environment variable names to
configuration colons. Replace `<deployment-managed-path>` with the SQLite file
location managed by the deployment. A missing, empty, malformed, or source-less
connection string stops startup with an error identifying the required setting.

## Persistence and migrations

Persistence uses Entity Framework Core 10 with SQLite. The context and provider
configuration live in `Kokishin.Infrastructure`; the API does not create or
migrate schemas on startup. The existing initial migration is an empty baseline
that creates only EF Core's migration metadata tables.

Install the EF command-line tool once, then apply the existing migrations:

```sh
dotnet tool install --global dotnet-ef --version 10.0.13
dotnet ef database update \
  --project Kokishin.Infrastructure \
  --startup-project Kokishin.Api
```

Create future migrations and apply them with:

```sh
dotnet ef migrations add AddPersistenceModel \
  --project Kokishin.Infrastructure \
  --startup-project Kokishin.Api \
  --output-dir Persistence/Migrations
dotnet ef database update \
  --project Kokishin.Infrastructure \
  --startup-project Kokishin.Api
```

Commit generated migrations as schema changes. The SQLite database and its
journal files are local development data and are ignored by Git. While the API
is running, check the configured database with
`curl --fail http://localhost:5108/health/database`.

## API error responses

The API returns errors using ASP.NET Core Problem Details (`application/problem+json`).
Responses include the HTTP `status`, a safe `title`, and a `traceId` when request
diagnostic information is available. Unexpected server errors use a generic
`detail`; internal exception messages and stack traces are not returned.
Validation failures use status `400` and include an `errors` object keyed by
field, while other unhandled HTTP error statuses use their corresponding status.

For example, an unexpected server error has this shape:

```json
{
  "type": "about:blank",
  "title": "An unexpected error occurred.",
  "status": 500,
  "detail": "The server encountered an unexpected error.",
  "traceId": "<request-trace-id>"
}
```

## Project documentation

Start with [`docs/README.md`](docs/README.md) for the documentation index.

- [Product vision](docs/goals/product-vision.md)
- [MVP scope](docs/goals/mvp-scope.md)
- [Requirements](docs/goals/requirements.md)
- [System context](docs/architecture/system-context.md)
- [Architecture decisions](docs/architecture/architecture-decisions.md)
- [UI design](docs/design/ui-design.md)
- [User workflows](docs/design/user-workflows.md)

The documentation describes the intended product and its initial direction.
Implementation details may change as the project is built.
