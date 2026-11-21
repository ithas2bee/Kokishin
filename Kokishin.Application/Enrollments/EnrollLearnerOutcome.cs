namespace Kokishin.Application.Enrollments;

public enum EnrollLearnerOutcome
{
    Created,
    AlreadyActive,
    ReEnrolled,
    InstructorNotAuthorized,
    InstructorInactive,
    CourseNotFound,
    LearnerNotFound,
    LearnerNotEligible,
    EnrollmentSuspended
}
