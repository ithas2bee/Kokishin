# Repository agent instructions

These instructions apply to the entire repository.

## Before changing code

1. Read this file and `README.md`, then inspect the root solution, the relevant
   project files, nearby implementation and tests, and any related product or
   architecture documentation.
2. Confirm the current project-reference rules in the `.csproj` files before
   proposing or documenting a dependency.
3. Identify what is already implemented. Treat roadmap, requirements, and
   diagrams as intended behavior, not as proof that backend functionality
   exists.

## Scope and architecture

- Implement only the assigned story. Do not add unrelated functionality or
  refactor unrelated code. Keep the .NET solution and its projects at the
  repository root; change that structure only when the issue explicitly requires
  it.
- Preserve the project boundaries and allowed project references documented in
  `README.md`. Do not change project references unless the issue explicitly
  requires it; if it appears necessary, explain the conflict before expanding
  scope.
- Keep business rules in Domain, use-case orchestration and owned abstractions
  in Application, HTTP delivery and composition in API, and external-system
  implementations in Infrastructure.
- Keep Infrastructure implementation details out of Application and keep
  Domain independent of all other application projects.
- Follow established neighboring conventions. Prefer small, cohesive changes
  and avoid adding dependencies, frameworks, or abstractions without a
  requirement.
- Keep error handling at the appropriate boundary: preserve domain invariants,
  do not silently suppress failures, and do not expose internal exception
  details to clients.

## Validation checklist

From the repository root, run:

```sh
dotnet build Kokishin.sln
dotnet test Kokishin.sln
```

There is no configured lint/format command. Verify that project references
still match `README.md`, inspect the final diff for unrelated changes, and
report any validation command that could not run and why.
