# Architecture Decisions

This file records architectural decisions and their rationale. Add a dated entry when a decision materially changes the design.

## AD-001: Use a layered application structure

**Status:** Accepted

**Decision:** Separate the API entry point, application use cases, domain rules, and infrastructure concerns. Application depends on Domain and owns abstractions for external concerns; Infrastructure implements those abstractions. The API is the composition root and may reference both Application and Infrastructure.

**Reasoning:** This keeps business rules testable and prevents persistence and other infrastructure details from controlling application or domain logic. The project boundaries are documented in the root [README](../../README.md#solution-structure).

## AD-002: Backend and web technology direction

**Status:** Accepted

**Decision:** Use C# and ASP.NET Core for the backend and React with TypeScript for the web interface. The backend projects currently target .NET 10.

**Reasoning:** This direction fits the project's intended maintainability and the developer's existing .NET experience while allowing a modern web UI.

## AD-003: Treat the MVP as a focused first release

**Status:** Accepted

**Decision:** Implement a small end-to-end instructor and learner experience before expanding the feature set.

**Reasoning:** A working, coherent workflow is more valuable for the initial project than a large collection of incomplete features.

## AD-004: Establish product identity independently

**Status:** Accepted

**Decision:** Use the name Kokishin and a forest-green visual identity. Use familiar LMS navigation patterns as inspiration, but do not use another product's logo or imply affiliation.

**Reasoning:** The interface should feel recognizable without misrepresenting ownership or copying another product's identity.

## AD-005: SQLite persistence foundation

**Status:** Accepted

**Decision:** Use Entity Framework Core with SQLite for the current persistence foundation. The provider configuration, context, and initial empty baseline migration live in Infrastructure.

**Reasoning:** SQLite keeps local development self-contained without a separately managed database service. A deployment that requires a server database or greater concurrency can replace the provider when supported by a defined use case.

## AD-006: Authentication architecture

**Status:** Accepted

**Decision:** Use a self-hosted OIDC provider with a same-origin API/BFF browser
session and Authorization Code with PKCE for future native clients. Map
provider identities to internal Kokishin user IDs and keep authorization in the
application.

**Reasoning:** This uses a standard identity protocol across the planned web
client and possible mobile clients without making Kokishin responsible for
password storage and recovery. The tradeoff is operating a security-critical
identity service in the home-hosted production environment. See the
[authentication decision record](../decisions/AD-006-authentication.md) for
alternatives, lifecycle, identity mapping, and operational requirements.
