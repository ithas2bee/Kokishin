namespace Kokishin.Application.Enrollments;

public enum EnrollmentChangeOutcome
{
    Changed,
    InstructorNotAuthorized,
    InstructorInactive,
    LearnerNotAuthorized,
    LearnerInactive,
    CourseNotFound,
    EnrollmentNotFound,
    InvalidTransition
}
