# AD-008: Initial course enrollment model

**Status:** Accepted

**Date:** 2026-11-17

## Context

The initial account role is either `Instructor` or `Learner`. Enrollment grants
access to a particular course and must not change or replace that account-level
role. Existing product requirements call for enrollment-based course access but
do not define how enrollment is created or managed.

## Decision

- Enrollment is instructor-managed, not self-service or invitation-based. Only
  the active Instructor who owns a course may explicitly enroll an active
  Learner account in that course. There is no separate approval step: the
  course owner's explicit enrollment action authorizes and activates membership.
  Course creation, publication, and account provisioning do not enroll anyone.
- Only Learner accounts may have learner enrollment. Enrollment is separate
  from the account's global role; an Instructor cannot be enrolled as a learner
  by changing or supplementing that role.
- Enrollment has four states: `Active`, `Suspended`, `Withdrawn`, and `Removed`.
  The allowed transitions are:

  | Current state | Action | Authorized actor | Result |
  |---|---|---|---|
  | No enrollment | Explicitly enroll | Course owner | `Active` |
  | `Active` | Suspend | Course owner | `Suspended` |
  | `Suspended` | Reactivate | Course owner | `Active` |
  | `Active` or `Suspended` | Withdraw | Enrolled Learner | `Withdrawn` |
  | `Active` or `Suspended` | Remove | Course owner | `Removed` |
  | `Withdrawn` or `Removed` | Explicitly re-enroll | Course owner | `Active` |

  No other state transitions are allowed. Withdrawal and removal change the
  enrollment status instead of deleting the enrollment record, account, or
  course work.
- There is at most one enrollment per course and learner account. Repeating an
  enrollment action for an already active enrollment does not create another
  record or change its state. To restore a suspended, withdrawn, or removed
  enrollment, the course owner must explicitly reactivate it.
- Learners may access course content only while their enrollment is `Active`
  and the course is `Published`. Draft courses are visible only to their owner.
  Changing a published course to Draft blocks learner access but does not alter
  enrollment states; access resumes if the course is republished and enrollment
  remains active.
- Course operations have the following access matrix. Every caller must have an
  active account, and the server derives the caller ID and role from the
  authenticated identity and persisted account:

  | Operation | Instructor | Learner |
  |---|---|---|
  | Create a course | Allowed; created course is owned by caller | Denied |
  | List courses (`GET /courses`) | Caller’s courses only | Denied |
  | Read a course (`GET /courses/{courseId}`) | Caller’s owned course | Only when published and caller has an `Active` enrollment |
  | Update course information or publication status | Caller’s owned course only | Denied |
  | Create, list, or change course enrollments | Caller’s owned course only | Denied |
  | Read or withdraw an enrollment through `/me/enrollments` | Denied | Caller’s own enrollment only |

  A missing course and an existing course the caller cannot read both return
  `404`, so course existence and private details are not disclosed. Course
  management operations on a course not owned by the Instructor also return
  `404`. The course read authorization is resource-based and checks persisted
  ownership or the Learner’s enrollment and course publication status.
- Enrollment authorization is enforced by the server using the persisted
  account role, status, course ownership, enrollment state, and course status.
  This decision adds no marketplace, payment, or institution integration.

## Consequences

The conceptual model must distinguish account role, course ownership, course
publication status, and enrollment status. Implementations must preserve one
enrollment per course/learner pair and enforce the state and access rules above.
This decision does not specify persistence fields, APIs, or user-interface
behavior.

## Unresolved questions

Before adding related capabilities, decide whether learners need a notification
or acceptance step, how long withdrawn/removed enrollment history is retained,
and how course ownership can be transferred. These questions do not change the
initial direct, owner-managed enrollment rule.
