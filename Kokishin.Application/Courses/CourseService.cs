using Kokishin.Domain.Courses;

namespace Kokishin.Application.Courses;

public sealed class CourseService(ICourseStore courseStore) : ICourseService
{
    public async Task<Course> CreateCourseAsync(
        Guid ownerId,
        string title,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var course = new Course(ownerId, title, description);
        await courseStore.AddAsync(course, cancellationToken);
        return course;
    }

    public Task<IReadOnlyList<Course>> ListOwnedCoursesAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        courseStore.ListByOwnerIdAsync(ownerId, cancellationToken);

    public Task<Course?> GetOwnedCourseAsync(
        Guid ownerId,
        Guid courseId,
        CancellationToken cancellationToken = default) =>
        courseStore.FindByIdAndOwnerIdAsync(courseId, ownerId, cancellationToken);

    public async Task<Course?> UpdateOwnedCourseAsync(
        Guid ownerId,
        Guid courseId,
        string title,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var course = await courseStore.FindByIdAndOwnerIdAsync(courseId, ownerId, cancellationToken);
        if (course is null)
        {
            return null;
        }

        course.UpdateInformation(title, description);
        await courseStore.UpdateAsync(course, cancellationToken);
        return course;
    }

    public async Task<Course?> ChangeOwnedCoursePublicationStatusAsync(
        Guid ownerId,
        Guid courseId,
        CourseStatus status,
        CancellationToken cancellationToken = default)
    {
        var course = await courseStore.FindByIdAndOwnerIdAsync(courseId, ownerId, cancellationToken);
        if (course is null)
        {
            return null;
        }

        course.ChangePublicationStatus(status);
        await courseStore.UpdateStatusAsync(course, cancellationToken);
        return course;
    }
}
