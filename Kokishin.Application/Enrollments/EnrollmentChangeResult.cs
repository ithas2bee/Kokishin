using Kokishin.Domain.Enrollments;

namespace Kokishin.Application.Enrollments;

public sealed record EnrollmentChangeResult(
    EnrollmentChangeOutcome Outcome,
    Enrollment? Enrollment = null);
