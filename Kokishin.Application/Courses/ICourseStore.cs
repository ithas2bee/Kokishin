using Kokishin.Domain.Courses;

namespace Kokishin.Application.Courses;

public interface ICourseStore
{
    Task AddAsync(Course course, CancellationToken cancellationToken = default);

    Task UpdateAsync(Course course, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(Course course, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Course>> ListByOwnerIdAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default);

    Task<Course?> FindCourseByIdAsync(Guid courseId, CancellationToken cancellationToken = default);

    Task<Course?> FindByIdAndOwnerIdAsync(
        Guid courseId,
        Guid ownerId,
        CancellationToken cancellationToken = default);
}
