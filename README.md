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

Install the .NET 10 SDK, then run these commands from the repository root to
restore, build, test, and start the API:

```sh
dotnet restore Kokishin.sln
dotnet build Kokishin.sln
dotnet test Kokishin.sln
dotnet run --project Kokishin.Api
```

See [backend testing conventions](docs/testing-conventions.md) for guidance on
organizing and running tests.

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

## Authentication

The API uses Authentik as its OIDC authority. Set the issuer URL, API audience,
confidential web client ID, registered callback URL, and confidential client
secret through configuration; all URLs must use HTTPS. No authentication
configuration is checked in. In Development, the API can start without
authentication settings for its current public health and OpenAPI endpoints,
but authentication is not enabled until all settings are supplied. Every
non-Development environment validates the settings at startup and stops with a
list of missing keys.

Initialize User Secrets for the API project (already initialized in this
repository) and supply local Authentik values:

```sh
dotnet user-secrets set "Authentication:Authority" "https://authentik.local/application/o/kokishin/" --project Kokishin.Api
dotnet user-secrets set "Authentication:Audience" "kokishin-api" --project Kokishin.Api
dotnet user-secrets set "Authentication:ClientId" "kokishin-web" --project Kokishin.Api
dotnet user-secrets set "Authentication:CallbackUrl" "https://localhost:5108/signin-oidc" --project Kokishin.Api
dotnet user-secrets set "Authentication:ClientSecret" "<local-client-secret>" --project Kokishin.Api
dotnet user-secrets set "DataProtection:KeysPath" "/var/lib/kokishin/development-keys" --project Kokishin.Api
dotnet user-secrets set "DataProtection:CertificatePath" "/path/to/development-key-encryption.pfx" --project Kokishin.Api
dotnet user-secrets set "DataProtection:CertificatePassword" "<local-certificate-password>" --project Kokishin.Api
```

In Production, provide `Authentication__Authority`,
`Authentication__Audience`, `Authentication__ClientId`, and
`Authentication__CallbackUrl` as deployment configuration, and provide
`Authentication__ClientSecret` from deployment-managed secret storage. Do not
put the confidential secret or production values in checked-in settings.

`GET /auth/login` starts the Authorization Code flow with PKCE. The callback
URL must match the Authentik client registration. The API stores provider
tokens in a Data Protection-protected ticket in the SQLite `SessionTickets`
table and sends the browser only an opaque `__Host-Kokishin.Session` cookie
marked `Secure`, `HttpOnly`, and `SameSite=Lax`. Cookie-authenticated
state-changing requests must include the request token returned by the
authenticated `GET /auth/csrf` endpoint in the `X-XSRF-TOKEN` header. The CSRF
cookie is `Secure`, `HttpOnly`, and `SameSite=Strict`.

The API persists Data Protection keys in the configured `DataProtection:KeysPath`
and encrypts them with the RSA certificate at `DataProtection:CertificatePath`.
Configure `DataProtection__KeysPath`, `DataProtection__CertificatePath`, and
`DataProtection__CertificatePassword` in Development User Secrets or
deployment-managed configuration/secrets. Use a protected, writable persistent
directory shared by all API instances, restrict its access to the API service,
and include the directory in protected backups. Keep the certificate and
password in deployment-managed secret storage; retain the certificate needed
to decrypt existing key-ring entries during rotation. API instances sharing
SQLite sessions must use the same persistent database, Data Protection key-ring
directory, encryption certificate, and `SetApplicationName("Kokishin")`.

Apply the session table migration before running the API:

```sh
dotnet ef database update --project Kokishin.Infrastructure --startup-project Kokishin.Api
```

JWT bearer tokens are validated through OIDC discovery and signing keys against
the configured issuer and audience. Authenticated principals expose a
deterministic internal Kokishin user ID in `ClaimTypes.NameIdentifier`,
derived from the validated `(issuer, subject)` pair; it is not the provider
subject or email. This mapping does not provision an application account or
grant roles or permissions.

The persistent account model uses the same stable internal ID and stores the
issuer and subject as a unique pair. It does not store email or provider-managed
credentials. Authentik owns credential verification, MFA, and recovery;
Kokishin owns explicit account provisioning and activation or deactivation.
Only a verified `(issuer, subject)` identity can be provisioned, new accounts
start inactive, and sign-in neither provisions accounts nor enables inactive
ones. Email is never used to match or provision an account.

## Persistence and migrations

Persistence uses Entity Framework Core 10 with SQLite. The context and provider
configuration live in `Kokishin.Infrastructure`; the API does not create or
migrate schemas on startup. The existing initial migration is an empty baseline
that creates only EF Core's migration metadata tables. The `Accounts` table
stores the stable account ID and its unique issuer/subject pair. The `Profiles`
table stores each account's required display name and optional description,
using the account ID as a one-to-one foreign key.

Install the EF command-line tool once, then apply all migrations:

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
Responses include the HTTP `status`, a safe `title`, and a `traceId`. The
`traceId` is the request's W3C `Activity.TraceId` when available, otherwise the
ASP.NET Core `HttpContext.TraceIdentifier`. ASP.NET Core parses valid incoming
W3C `traceparent` context and generates an Activity trace ID when context is
missing or invalid; arbitrary request header values are not echoed. Unexpected
server errors use a generic `detail`; internal exception messages and stack
traces are not returned.
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

Use the returned `traceId` to find related request log entries. For example, if
a client reports `traceId` `4bf92f3577b34da6a3ce929d0e0e4736`, search the JSON
console logs for that value in the Activity scope's `TraceId`. When a valid
`traceparent` is supplied, its validated trace ID is used; otherwise ASP.NET
Core generates the request Activity context. Invalid trace headers do not stop
request processing, and untrusted header contents are not used as diagnostic
identifiers.

## OpenAPI

The API generates an OpenAPI document from its registered endpoints. It is
available in Development at `/openapi/v1.json`; production exposure is disabled
by default and can be explicitly enabled with `OpenApi:ExposeInProduction=true`.
See the [OpenAPI guide](docs/openapi.md) for access details, the current API
contract, and verification instructions.

## Application logging

The API uses the built-in `Microsoft.Extensions.Logging` abstractions and the
ASP.NET Core console provider. Console output uses the built-in JSON formatter,
including structured log state and exception details, with UTC timestamps. The
formatter includes logging scopes; the logger factory adds Activity `TraceId`,
`SpanId`, and `ParentId` values to those scopes. The default levels are
`Information` for application events and `Warning` for `Microsoft.AspNetCore`;
the Development configuration follows the same levels.

Logging levels and formatter options follow ASP.NET Core configuration
precedence. Set values in `appsettings.{Environment}.json` to override shared
settings for an environment, or use environment variables (which take
precedence), for example:

```sh
export Logging__LogLevel__Default=Debug
export Logging__LogLevel__Microsoft.AspNetCore=Information
```

Use `Trace` or `Debug` only when detailed diagnostics are needed, `Information`
for operational events, `Warning` for recoverable unexpected conditions, and
`Error` for failed operations. Keep messages stable and pass variable values as
named structured properties rather than interpolating them into the message.
Unhandled exceptions are logged server-side by ASP.NET Core's exception
handling middleware with exception details; the API response remains the safe
Problem Details shape described above.

Never log passwords, credentials, tokens, API keys, connection strings, or
sensitive personal information. Do not log complete request or response
bodies, and redact sensitive values from any diagnostic data or exception
messages before logging.

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
