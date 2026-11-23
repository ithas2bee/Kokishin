using Kokishin.Domain.Enrollments;

namespace Kokishin.Application.Enrollments;

public interface IEnrollmentStore
{
    Task<Enrollment?> FindAsync(
        Guid courseId,
        Guid learnerAccountId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Enrollment>> ListByCourseIdAsync(
        Guid courseId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Enrollment>> ListByLearnerAccountIdAsync(
        Guid learnerAccountId,
        CancellationToken cancellationToken = default);

    Task<Enrollment?> AddIfAbsentAsync(
        Enrollment enrollment,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(Enrollment enrollment, CancellationToken cancellationToken = default);
}
