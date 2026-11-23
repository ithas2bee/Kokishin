namespace Kokishin.Application.Enrollments;

public interface IEnrollmentService
{
    Task<EnrollLearnerResult> EnrollLearnerAsync(
        Guid instructorId,
        Guid courseId,
        Guid learnerAccountId,
        CancellationToken cancellationToken = default);
}
