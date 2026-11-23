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

    Task<Course?> GetCourseAsync(Guid courseId, CancellationToken cancellationToken = default);

    Task<Course?> GetOwnedCourseAsync(
        Guid ownerId,
        Guid courseId,
        CancellationToken cancellationToken = default);

    Task<Course?> UpdateOwnedCourseAsync(
        Guid ownerId,
        Guid courseId,
        string title,
        string? description = null,
        CancellationToken cancellationToken = default);

    Task<Course?> ChangeOwnedCoursePublicationStatusAsync(
        Guid ownerId,
        Guid courseId,
        CourseStatus status,
        CancellationToken cancellationToken = default);
}
