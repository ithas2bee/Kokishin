# Backend Testing Conventions

`Kokishin.Tests` is the central backend test project and is included in the root
`Kokishin.sln`. Keep the existing solution and project boundaries; add another
test project only when a documented technical reason requires it.

## Organization and naming

Organize test files under folders named for the component or behavior they cover,
such as `Api/` and `Infrastructure/`. Name test classes after the component and
group related behaviors in that class. Use
`MethodOrBehavior_Scenario_ExpectedOutcome` for test methods, for example
`AddInfrastructure_WithoutConnectionString_ThrowsInvalidOperationException`.
The test class identifies the component; the method name identifies the behavior
and expected outcome. For theories, the method name should describe the behavior
shared by its cases.

## Arrange, Act, Assert

Structure each test in this order:

1. **Arrange** the system under test, inputs, and any required test-only
   configuration.
2. **Act** by invoking one primary behavior.
3. **Assert** the observable result with clear assertions.

Keep setup relevant to the behavior and avoid order-dependent tests. Comments
marking Arrange, Act, and Assert are optional when the sections are already
obvious. Prefer public behavior over private implementation details; use
assertions that report the expected and actual values clearly.

## Unit tests

Use unit tests for an individual component or behavior that can be exercised
without external infrastructure. Keep them fast and independent of the
developer's environment. Use a test double only when it provides meaningful
isolation; avoid mocking dependencies that do not need to be controlled.

## Integration tests

Use integration tests when behavior depends on multiple application components,
the HTTP pipeline, persistence, or another system boundary. API integration
tests may use ASP.NET Core `WebApplicationFactory` and should verify observable
HTTP behavior. Add test-only endpoints or middleware through the test host, not
to production API functionality. Keep test coverage focused; this guidance does
not require building a broader integration-test framework.

## Configuration and database isolation

Provide explicit test configuration instead of relying on a developer's
environment, local files, or normal Kokishin database. For API tests, set an
appropriate test-host environment and override required settings in the host.
The existing API tests use the `Production` environment and
`Data Source=:memory:` for the SQLite connection.

Database-dependent tests must use an isolated database and test data. Prefer
SQLite in-memory storage when its lifetime semantics fit the test. SQLite
in-memory databases are scoped to their connection: if a test needs the
database to survive multiple context connections, own and keep open the
connection for the fixture's lifetime, then dispose it. If persistence to a
file is necessary, give each test a unique temporary database and clean it up
during disposal. A fixture may own reusable
infrastructure, but it must not share mutable database state between tests. Do
not use `kokishin.db` or a developer-specific filesystem path. Do not add a
database provider solely for tests.

## Fixtures and shared utilities

Keep test setup local and straightforward. Use xUnit fixtures for expensive
resources that are intentionally shared within a test class, and ensure the
fixture owns and disposes those resources. Do not share mutable database state
between tests. Extract a helper only after repeated setup makes the tests
materially clearer; avoid abstractions that hide the Arrange/Act/Assert flow.

## Architecture boundary tests

`Kokishin.Tests/Architecture/ProjectReferenceArchitectureTests.cs` reads each
root-level project's `.csproj` file and inspects its `ProjectReference` entries.
The enforced references are:

| Project | Required project references |
| --- | --- |
| `Kokishin.Domain` | None |
| `Kokishin.Application` | `Kokishin.Domain` |
| `Kokishin.Infrastructure` | `Kokishin.Application`, `Kokishin.Domain` |
| `Kokishin.Api` | `Kokishin.Application`, `Kokishin.Infrastructure` |
| `Kokishin.Tests` | `Kokishin.Api`, `Kokishin.Infrastructure` |

The test project references the API for its host-based tests and Infrastructure
for tests that directly exercise infrastructure registration. The architecture
tests locate `Kokishin.sln` by walking up from the test assembly's output
directory, so they do not depend on the current working directory or a
developer-specific path.

The checks report unexpected and missing references with the affected projects
and rule. A graph traversal also detects direct and indirect circular
dependencies and reports the dependency cycle. Isolated temporary `.csproj`
fixtures verify that violations and cycles are detected without changing the
solution's project files. Run the checks with `dotnet test Kokishin.sln` from
the repository root. When a check fails, remove the invalid reference or update
the architecture decision and corresponding rules together if the intended
architecture has changed.

## Running and diagnosing tests

Run commands from the repository root. The full suite runs through the solution:

```sh
dotnet restore Kokishin.sln
dotnet build Kokishin.sln
dotnet test Kokishin.sln
```

To investigate a failure, first read the failing test name and assertion output.
Rerun the affected test by filtering its fully qualified name, then run the
complete solution suite after changes:

```sh
dotnet test Kokishin.sln --filter "FullyQualifiedName~TestMethodName"
dotnet test Kokishin.sln
```

Keep assertions specific, preserve unexpected exceptions, and include relevant
expected/actual context in the assertion rather than suppressing failures.
