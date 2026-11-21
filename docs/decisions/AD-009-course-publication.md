# AD-009: Initial course publication lifecycle

**Status:** Accepted

**Date:** 2026-11-21

## Context

Courses need a clear lifecycle so an Instructor can prepare a course before
making it available to enrolled Learners. Publication must not imply public
access, create enrollments, or bypass the enrollment rules in
[AD-008](AD-008-course-enrollment.md).

## Decision

- The initial lifecycle has two states: `Draft` and `Published`. No `Archived`
  state is added.
- The owning Instructor may change a course between `Draft` and `Published`:

  | Current state | Action | Result |
  |---|---|---|
  | `Draft` | Publish | `Published` |
  | `Published` | Return to draft | `Draft` |

- No other transition is valid. Requesting the current state or an unsupported
  status is rejected.
- Only the active Instructor who owns the course may change its publication
  status. The server derives the account identity from authentication and
  verifies ownership against persisted course data.
- Draft courses are available only to their owner. Published status alone does
  not grant Learners access; access continues to require an active enrollment.
- Changing publication status does not create, delete, or change enrollment
  records. Returning a course to Draft blocks Learner access without altering
  enrollment state; republishing restores access for eligible active
  enrollments.

## Consequences

The status transition rule belongs in the Domain model. The API exposes status
management only to authenticated Instructors, and the application scopes the
operation to the authenticated owner. No database migration is needed when
the existing `Courses.Status` column can store these two states.
