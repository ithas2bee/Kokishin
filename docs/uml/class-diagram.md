# Class Diagram

This conceptual domain model identifies likely core entities. Names, relationships, and fields may change during detailed design.

```mermaid
classDiagram
    class User {
        +Id
        +DisplayName
        +Email
    }
    class Course {
        +Id
        +Title
        +Description
        +Status
    }
    class Enrollment {
        +Id
        +Role
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

    User "1" --> "0..*" Enrollment : has
    Course "1" --> "0..*" Enrollment : includes
    Course "1" --> "0..*" Module : contains
    Module "1" --> "0..*" LearningPage : contains
    Course "1" --> "0..*" Announcement : publishes
    Course "1" --> "0..*" Assignment : defines
    Assignment "1" --> "0..*" Submission : receives
    User "1" --> "0..*" Submission : creates
    Submission "1" --> "0..1" Feedback : receives
    User "1" --> "0..*" Feedback : provides
```

## Notes

- This is a conceptual model, not a final database schema.
- Authentication details, file storage, discussion threads, rubrics, quizzes, and audit fields need further specification before implementation.
