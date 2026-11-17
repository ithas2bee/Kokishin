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
}
