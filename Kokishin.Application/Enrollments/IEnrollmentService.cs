using Kokishin.Domain.Enrollments;

namespace Kokishin.Application.Enrollments;

public interface IEnrollmentService
{
    Task<IReadOnlyList<Enrollment>?> ListForOwnedCourseAsync(
        Guid instructorId,
        Guid courseId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Enrollment>> ListForLearnerAsync(
        Guid learnerAccountId,
        CancellationToken cancellationToken = default);

    Task<Enrollment?> GetForLearnerAsync(
        Guid learnerAccountId,
        Guid courseId,
        CancellationToken cancellationToken = default);

    Task<EnrollLearnerResult> EnrollLearnerAsync(
        Guid instructorId,
        Guid courseId,
        Guid learnerAccountId,
        CancellationToken cancellationToken = default);

    Task<EnrollmentChangeResult> ChangeOwnedEnrollmentAsync(
        Guid instructorId,
        Guid courseId,
        Guid learnerAccountId,
        EnrollmentAction action,
        CancellationToken cancellationToken = default);

    Task<EnrollmentChangeResult> WithdrawOwnEnrollmentAsync(
        Guid learnerAccountId,
        Guid courseId,
        CancellationToken cancellationToken = default);
}
