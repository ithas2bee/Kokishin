using Kokishin.Domain.Courses;

namespace Kokishin.Application.Courses;

public interface ICourseService
{
    Task<Course> CreateCourseAsync(
        Guid ownerId,
        string title,
        string? description = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Course>> ListOwnedCoursesAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default);

    Task<Course?> GetOwnedCourseAsync(
        Guid ownerId,
        Guid courseId,
        CancellationToken cancellationToken = default);
}
