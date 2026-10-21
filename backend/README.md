# Backend

The backend is a .NET solution containing an ASP.NET Core API and three class
libraries. Build it from this directory with:

```sh
dotnet build Kokishin.Backend.sln
```

## Projects and responsibilities

- **Kokishin.Api** (`src/Kokishin.Api`): HTTP delivery and the composition root.
  It configures the application and infrastructure implementations. It contains
  no product endpoints yet.
- **Kokishin.Application** (`src/Kokishin.Application`): application use cases
  and the interfaces they need from infrastructure.
- **Kokishin.Domain** (`src/Kokishin.Domain`): business concepts and rules,
  independent of delivery, persistence, and frameworks.
- **Kokishin.Infrastructure** (`src/Kokishin.Infrastructure`): implementations
  of application interfaces and external technical concerns, including future
  persistence.

## Dependency rules

- Domain has no project dependencies.
- Application may depend on Domain, but not on API or Infrastructure.
- Infrastructure may depend on Application and Domain to implement their
  abstractions.
- API may depend on Application and Infrastructure to compose the application.

This keeps infrastructure details behind abstractions owned by the application
and leaves business rules independent of external technologies. Add a
feature's business rules to Domain, coordinate its use cases in Application,
and add delivery or external-system code only to the corresponding outer
project. No feature, database provider, or ORM is included in this foundation.
