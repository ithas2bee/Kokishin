using Kokishin.Domain.Courses;

namespace Kokishin.Application.Courses;

public interface ICourseService
{
    Task<Course> CreateCourseAsync(
        Guid ownerId,
        string title,
        string? description = null,
        CancellationToken cancellationToken = default);
}
