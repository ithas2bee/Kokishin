# Use-Case Diagram

The diagram captures the initial MVP use cases. It is a conceptual view, not a finalized implementation specification.

```mermaid
flowchart LR
    Instructor[Instructor]
    Learner[Learner]

    subgraph Kokishin
        CreateCourse((Create and manage course))
        OrganizeContent((Organize modules and pages))
        Announce((Publish announcements))
        CreateAssignment((Create assignment))
        ReviewSubmission((Review submission))
        GiveFeedback((Give grade and feedback))
        ViewCourse((View course content))
        Participate((Participate in discussion))
        SubmitWork((Submit assignment))
        ViewFeedback((View grades and feedback))
    end

    Instructor --> CreateCourse
    Instructor --> OrganizeContent
    Instructor --> Announce
    Instructor --> CreateAssignment
    Instructor --> ReviewSubmission
    Instructor --> GiveFeedback
    Learner --> ViewCourse
    Learner --> Participate
    Learner --> SubmitWork
    Learner --> ViewFeedback
```

## Notes

- Instructor and learner are the initial primary roles.
- Authentication, enrollment, and authorization support these use cases.
- Additional use cases should be added only when included in an approved scope.
