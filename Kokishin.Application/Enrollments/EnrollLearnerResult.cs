using Kokishin.Domain.Enrollments;

namespace Kokishin.Application.Enrollments;

public sealed record EnrollLearnerResult(EnrollLearnerOutcome Outcome, Enrollment? Enrollment = null);
