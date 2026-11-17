# Class Diagram

This conceptual domain model identifies likely core entities. Names, relationships, and fields may change during detailed design.

```mermaid
classDiagram
    class Account {
        +Id
        +Role
    }
    class Course {
        +Id
        +Title
        +Description
        +Status
    }
    class Enrollment {
        +Id
        +Status
    }
    class Module {
        +Id
        +Title
        +Position
    }
    class LearningPage {
        +Id
        +Title
        +Content
        +Position
    }
    class Announcement {
        +Id
        +Title
        +Content
        +PublishedAt
    }
    class Assignment {
        +Id
        +Title
        +Instructions
        +DueAt
    }
    class Submission {
        +Id
        +SubmittedAt
        +ContentReference
    }
    class Feedback {
        +Id
        +Comment
        +Grade
        +CreatedAt
    }

    Account "1" --> "0..*" Enrollment : has
    Account "1" --> "0..*" Course : owns
    Course "1" --> "0..*" Enrollment : includes
    Course "1" --> "0..*" Module : contains
    Module "1" --> "0..*" LearningPage : contains
    Course "1" --> "0..*" Announcement : publishes
    Course "1" --> "0..*" Assignment : defines
    Assignment "1" --> "0..*" Submission : receives
    Account "1" --> "0..*" Submission : creates
    Submission "1" --> "0..1" Feedback : receives
    Account "1" --> "0..*" Feedback : provides
```

## Notes

- This is a conceptual model, not a final database schema.
- `Account.Role` is the account-level instructor/learner role; enrollment does
  not assign roles or represent course ownership. Only Learner accounts may be
  enrolled.
- `Enrollment.Status` is `Active`, `Suspended`, `Withdrawn`, or `Removed`.
  Course owners manage enrollment, and learners may withdraw their own
  enrollment. See [AD-008](../decisions/AD-008-course-enrollment.md) for the
  complete lifecycle and course-access rules.
- Authentication details, file storage, discussion threads, rubrics, quizzes, and audit fields need further specification before implementation.
