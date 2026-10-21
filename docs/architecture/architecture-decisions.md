# Architecture Decisions

This file records architectural decisions and their rationale. Add a dated entry when a decision materially changes the design.

## AD-001: Use a layered application structure

**Status:** Proposed

**Decision:** Separate the web/API entry points, application use cases, domain rules, and infrastructure concerns.

**Reasoning:** This keeps business rules testable and reduces coupling between course behavior, the user interface, and persistence.

## AD-002: Initial technology direction

**Status:** Proposed

**Decision:** Use C# and ASP.NET Core for the backend, React with TypeScript for the web interface, and SQL Server with Entity Framework Core for persistence.

**Reasoning:** This direction fits the project's intended maintainability and the developer's existing .NET experience while allowing a modern web UI.

## AD-003: Treat the MVP as a focused first release

**Status:** Accepted

**Decision:** Implement a small end-to-end instructor and learner experience before expanding the feature set.

**Reasoning:** A working, coherent workflow is more valuable for the initial project than a large collection of incomplete features.

## AD-004: Establish product identity independently

**Status:** Accepted

**Decision:** Use the name Kokishin and a forest-green visual identity. Use familiar LMS navigation patterns as inspiration, but do not use another product's logo or imply affiliation.

**Reasoning:** The interface should feel recognizable without misrepresenting ownership or copying another product's identity.
